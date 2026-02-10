using System.Collections.Concurrent;
using System.Text;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that processes glucose period comparisons.
/// When a new comparison is created, it gathers glucose data and events for both periods,
/// computes statistics, calls GPT for a differential analysis, and notifies the UI.
/// </summary>
public class ComparisonService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComparisonService> _logger;
    private readonly INotificationService _notifications;
    private readonly ConcurrentQueue<int> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public ComparisonService(
        IServiceProvider serviceProvider,
        ILogger<ComparisonService> logger,
        INotificationService notifications)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
    }

    /// <summary>
    /// Enqueue a comparison for background processing. Called after creating the DB row.
    /// </summary>
    public void Enqueue(int comparisonId)
    {
        _queue.Enqueue(comparisonId);
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ComparisonService started.");

        // Also pick up any comparisons that were left in "pending" or "processing" state (e.g., after a restart)
        await RequeueUnfinishedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_queue.TryDequeue(out var comparisonId))
            {
                try
                {
                    await ProcessComparisonAsync(comparisonId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to process comparison {Id}.", comparisonId);
                    await SetFailedAsync(comparisonId, ex.Message, stoppingToken);
                }
            }
        }
    }

    private async Task RequeueUnfinishedAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

            var unfinished = await db.GlucoseComparisons
                .Where(c => c.Status == "pending" || c.Status == "processing")
                .Select(c => c.Id)
                .ToListAsync(ct);

            foreach (var id in unfinished)
            {
                _queue.Enqueue(id);
                _signal.Release();
            }

            if (unfinished.Count > 0)
                _logger.LogInformation("Re-queued {Count} unfinished comparison(s).", unfinished.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-queue unfinished comparisons on startup.");
        }
    }

    private async Task SetFailedAsync(int comparisonId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
            var comp = await db.GlucoseComparisons.FindAsync(new object[] { comparisonId }, ct);
            if (comp != null)
            {
                comp.Status = "failed";
                comp.ErrorMessage = error.Length > 2000 ? error[..2000] : error;
                comp.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            await _notifications.NotifyComparisonsUpdatedAsync(1, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not mark comparison {Id} as failed.", comparisonId);
        }
    }

    private async Task ProcessComparisonAsync(int comparisonId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var timeZoneConverter = scope.ServiceProvider.GetRequiredService<TimeZoneConverter>();
        var gptClient = scope.ServiceProvider.GetRequiredService<IGptClient>();

        var comp = await db.GlucoseComparisons.FindAsync(new object[] { comparisonId }, ct);
        if (comp == null)
        {
            _logger.LogWarning("Comparison {Id} not found.", comparisonId);
            return;
        }

        comp.Status = "processing";
        await db.SaveChangesAsync(ct);
        await _notifications.NotifyComparisonsUpdatedAsync(1, ct);

        var analysisSettings = await settingsService.GetAnalysisSettingsAsync();
        var tz = timeZoneConverter.Resolve(analysisSettings.TimeZone);
        comp.TimeZone = analysisSettings.TimeZone;

        // ── Gather data for Period A ─────────────────────────
        var (readingsA, eventsA, statsA) = await GatherPeriodDataAsync(db, comp.PeriodAStart, comp.PeriodAEnd, ct);
        comp.PeriodAReadingCount = statsA.ReadingCount;
        comp.PeriodAGlucoseMin = statsA.Min;
        comp.PeriodAGlucoseMax = statsA.Max;
        comp.PeriodAGlucoseAvg = statsA.Avg;
        comp.PeriodAGlucoseStdDev = statsA.StdDev;
        comp.PeriodATimeInRange = statsA.TimeInRange;
        comp.PeriodATimeAboveRange = statsA.TimeAboveRange;
        comp.PeriodATimeBelowRange = statsA.TimeBelowRange;
        comp.PeriodAEventCount = eventsA.Count;
        comp.PeriodAEventTitles = string.Join(" | ", eventsA.Select(e => e.NoteTitle));

        // ── Gather data for Period B ─────────────────────────
        var (readingsB, eventsB, statsB) = await GatherPeriodDataAsync(db, comp.PeriodBStart, comp.PeriodBEnd, ct);
        comp.PeriodBReadingCount = statsB.ReadingCount;
        comp.PeriodBGlucoseMin = statsB.Min;
        comp.PeriodBGlucoseMax = statsB.Max;
        comp.PeriodBGlucoseAvg = statsB.Avg;
        comp.PeriodBGlucoseStdDev = statsB.StdDev;
        comp.PeriodBTimeInRange = statsB.TimeInRange;
        comp.PeriodBTimeAboveRange = statsB.TimeAboveRange;
        comp.PeriodBTimeBelowRange = statsB.TimeBelowRange;
        comp.PeriodBEventCount = eventsB.Count;
        comp.PeriodBEventTitles = string.Join(" | ", eventsB.Select(e => e.NoteTitle));

        await db.SaveChangesAsync(ct);

        // ── AI analysis ──────────────────────────────────────
        if (!analysisSettings.IsConfigured || string.IsNullOrWhiteSpace(analysisSettings.GptApiKey))
        {
            _logger.LogWarning("GPT API key not configured. Saving comparison without AI analysis.");
            comp.Status = "completed";
            comp.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await _notifications.NotifyComparisonsUpdatedAsync(1, ct);
            return;
        }

        var (systemPrompt, userPrompt) = BuildComparisonPrompts(comp, readingsA, eventsA, readingsB, eventsB, tz);

        const string modelName = "gpt-5-mini";
        var gptResult = await gptClient.AnalyzeAsync(
            analysisSettings.GptApiKey, systemPrompt, userPrompt, modelName, 4096, ct);

        // Log AI usage
        await LogUsageAsync(db, comp, gptResult, ct);

        if (gptResult.Success && !string.IsNullOrWhiteSpace(gptResult.Content))
        {
            var (cleanAnalysis, classification) = ClassificationParser.Parse(gptResult.Content);
            comp.AiAnalysis = cleanAnalysis;
            comp.AiClassification = classification;
        }

        comp.Status = "completed";
        comp.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Comparison {Id} completed. Period A: {AReadings} readings/{AEvents} events, Period B: {BReadings} readings/{BEvents} events.",
            comp.Id, readingsA.Count, eventsA.Count, readingsB.Count, eventsB.Count);

        await _notifications.NotifyComparisonsUpdatedAsync(1, ct);
        await _notifications.NotifyAiUsageUpdatedAsync(1, ct);
    }

    // ────────────────────────────────────────────────────────────
    // Data Gathering
    // ────────────────────────────────────────────────────────────

    private static async Task<(List<GlucoseReading> readings, List<GlucoseEvent> events, DayGlucoseStats stats)>
        GatherPeriodDataAsync(GlucoseDbContext db, DateTime start, DateTime end, CancellationToken ct)
    {
        var readings = await db.GlucoseReadings
            .Where(r => r.Timestamp >= start && r.Timestamp < end)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var events = await db.GlucoseEvents
            .Where(e => e.EventTimestamp >= start && e.EventTimestamp < end)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        var stats = GlucoseStatsCalculator.ComputeDayStats(readings);

        return (readings, events, stats);
    }

    // ────────────────────────────────────────────────────────────
    // Prompt Building
    // ────────────────────────────────────────────────────────────

    private static (string systemPrompt, string userPrompt) BuildComparisonPrompts(
        GlucoseComparison comp,
        List<GlucoseReading> readingsA, List<GlucoseEvent> eventsA,
        List<GlucoseReading> readingsB, List<GlucoseEvent> eventsB,
        TimeZoneInfo tz)
    {
        var sb = new StringBuilder();

        var labelA = !string.IsNullOrWhiteSpace(comp.PeriodALabel) ? comp.PeriodALabel : "Period A";
        var labelB = !string.IsNullOrWhiteSpace(comp.PeriodBLabel) ? comp.PeriodBLabel : "Period B";

        // ── Period A ────────────────────────────────────────
        sb.AppendLine($"=== {labelA.ToUpper()} ===");
        sb.AppendLine($"From: {TimeZoneConverter.ToLocal(comp.PeriodAStart, tz):yyyy-MM-dd HH:mm}");
        sb.AppendLine($"To:   {TimeZoneConverter.ToLocal(comp.PeriodAEnd, tz):yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Duration: {FormatDuration(comp.PeriodAEnd - comp.PeriodAStart)}");
        AppendPeriodStats(sb, comp.PeriodAReadingCount, comp.PeriodAGlucoseMin, comp.PeriodAGlucoseMax,
            comp.PeriodAGlucoseAvg, comp.PeriodAGlucoseStdDev, comp.PeriodATimeInRange,
            comp.PeriodATimeAboveRange, comp.PeriodATimeBelowRange);
        AppendPeriodEvents(sb, eventsA, tz);
        AppendPeriodTimeline(sb, readingsA, tz);
        sb.AppendLine();

        // ── Period B ────────────────────────────────────────
        sb.AppendLine($"=== {labelB.ToUpper()} ===");
        sb.AppendLine($"From: {TimeZoneConverter.ToLocal(comp.PeriodBStart, tz):yyyy-MM-dd HH:mm}");
        sb.AppendLine($"To:   {TimeZoneConverter.ToLocal(comp.PeriodBEnd, tz):yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Duration: {FormatDuration(comp.PeriodBEnd - comp.PeriodBStart)}");
        AppendPeriodStats(sb, comp.PeriodBReadingCount, comp.PeriodBGlucoseMin, comp.PeriodBGlucoseMax,
            comp.PeriodBGlucoseAvg, comp.PeriodBGlucoseStdDev, comp.PeriodBTimeInRange,
            comp.PeriodBTimeAboveRange, comp.PeriodBTimeBelowRange);
        AppendPeriodEvents(sb, eventsB, tz);
        AppendPeriodTimeline(sb, readingsB, tz);

        var dataText = sb.ToString();

        var systemPrompt = $@"You are a diabetes management assistant comparing TWO TIME PERIODS of glucose data.
The user wants to understand how their glucose control differed between these two periods and what caused the differences.

IMPORTANT: Your response MUST start with a classification line in this exact format:
[CLASSIFICATION: green]
or [CLASSIFICATION: yellow] or [CLASSIFICATION: red]

Classification guide (based on the COMPARISON outcome):
- **green**: The comparison shows improvement or both periods were well-controlled. Positive trend.
- **yellow**: Mixed results — some metrics improved, others worsened. Or both periods had moderate control.
- **red**: The comparison shows deterioration, or both periods had poor control. Concerning trend.

After the classification line, your comparison analysis should include these sections (use the exact bold headings):
1. **Overview**: High-level summary of the comparison — which period was better controlled and by how much?
2. **Key Metrics Comparison**: Compare average glucose, time in range, variability (std dev), min/max, and time above/below range side by side. Highlight the most significant differences.
3. **Event Analysis**: Compare the meals and activities between the two periods. Were there different types of foods, different timing, more/less activity? How did the events in each period affect glucose differently?
4. **Pattern Differences**: What glucose patterns differed? (e.g., overnight stability, post-meal spikes, morning fasting levels, afternoon dips)
5. **What Caused the Difference**: Based on the events, timing, and glucose patterns, explain what likely caused the differences. Be specific — reference individual events when possible.
6. **Actionable Insights**: 2-3 practical suggestions based on what worked well in the better period and what could be improved.

Keep the analysis concise but insightful. Use markdown formatting.
Do not include a title heading. Use mg/dL units. All timestamps are in the user's local time.
Write in a friendly, supportive tone — like a knowledgeable health coach.";

        var userPrompt = $@"Please compare these two glucose monitoring periods:
{(comp.Name != null ? $"\nComparison name: {comp.Name}" : "")}

{dataText}";

        return (systemPrompt, userPrompt);
    }

    private static void AppendPeriodStats(StringBuilder sb, int readingCount,
        double? min, double? max, double? avg, double? stdDev,
        double? tir, double? tar, double? tbr)
    {
        sb.AppendLine($"  Readings: {readingCount}");
        if (readingCount > 0)
        {
            sb.AppendLine($"  Glucose range: {min} – {max} mg/dL");
            sb.AppendLine($"  Average: {avg} mg/dL");
            sb.AppendLine($"  Std deviation: {stdDev} mg/dL");
            sb.AppendLine($"  Time in range (70-180): {tir}%");
            sb.AppendLine($"  Time above range (>180): {tar}%");
            sb.AppendLine($"  Time below range (<70): {tbr}%");
        }
    }

    private static void AppendPeriodEvents(StringBuilder sb, List<GlucoseEvent> events, TimeZoneInfo tz)
    {
        sb.AppendLine($"  Events: {events.Count}");
        foreach (var evt in events)
        {
            var localTime = TimeZoneConverter.ToLocal(evt.EventTimestamp, tz);
            sb.AppendLine($"    • {localTime:yyyy-MM-dd HH:mm} — {evt.NoteTitle}");
            if (!string.IsNullOrWhiteSpace(evt.NoteContent))
                sb.AppendLine($"      Content: {Truncate(evt.NoteContent, 200)}");
            if (evt.GlucoseAtEvent.HasValue)
                sb.AppendLine($"      Glucose: {evt.GlucoseAtEvent} mg/dL, Spike: {(evt.GlucoseSpike.HasValue ? $"+{evt.GlucoseSpike}" : "N/A")} mg/dL");
            if (evt.AiClassification != null)
                sb.AppendLine($"      Classification: {evt.AiClassification}");
        }
    }

    private static void AppendPeriodTimeline(StringBuilder sb, List<GlucoseReading> readings, TimeZoneInfo tz)
    {
        if (readings.Count == 0) return;

        sb.AppendLine("  Glucose timeline (sampled):");
        var sample = readings.Count > 40
            ? readings.Where((_, i) => i % (readings.Count / 30 + 1) == 0).ToList()
            : readings;
        foreach (var r in sample)
        {
            sb.AppendLine($"    {TimeZoneConverter.ToLocal(r.Timestamp, tz):yyyy-MM-dd HH:mm} → {r.Value} mg/dL");
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours < 24) return $"{ts.TotalHours:F1} hours";
        return $"{ts.TotalDays:F1} days ({ts.TotalHours:F0} hours)";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength] + "…";
    }

    // ────────────────────────────────────────────────────────────
    // AI Usage Logging
    // ────────────────────────────────────────────────────────────

    private async Task LogUsageAsync(
        GlucoseDbContext db, GlucoseComparison comp, GptAnalysisResult result, CancellationToken ct)
    {
        try
        {
            db.AiUsageLogs.Add(new AiUsageLog
            {
                GlucoseEventId = null,
                Model = result.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                TotalTokens = result.TotalTokens,
                Reason = $"Period comparison #{comp.Id}" + (comp.Name != null ? $": {comp.Name}" : ""),
                Success = result.Success,
                HttpStatusCode = result.HttpStatusCode,
                FinishReason = result.FinishReason,
                CalledAt = DateTime.UtcNow,
                DurationMs = result.DurationMs
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log AI usage for comparison {Id}.", comp.Id);
        }
    }
}
