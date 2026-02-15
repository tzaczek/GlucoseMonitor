using System.Collections.Concurrent;
using System.Text;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using static GlucoseAPI.Application.Interfaces.EventCategory;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that processes period summaries.
/// When a new period summary is created, it gathers glucose data and events
/// for the requested time range, computes statistics, calls GPT for a
/// comprehensive analysis, and notifies the UI via SignalR.
/// </summary>
public class PeriodSummaryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PeriodSummaryService> _logger;
    private readonly INotificationService _notifications;
    private readonly ConcurrentQueue<(int Id, string? ModelOverride)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    private readonly IEventLogger _eventLogger;

    public PeriodSummaryService(
        IServiceProvider serviceProvider,
        ILogger<PeriodSummaryService> logger,
        INotificationService notifications,
        IEventLogger eventLogger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// Enqueue a period summary for background processing. Called after creating the DB row.
    /// </summary>
    public void Enqueue(int summaryId, string? modelOverride = null)
    {
        _queue.Enqueue((summaryId, modelOverride));
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PeriodSummaryService started.");

        // Pick up any summaries left in "pending" or "processing" state (e.g., after restart)
        await RequeueUnfinishedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_queue.TryDequeue(out var item))
            {
                try
                {
                    await ProcessSummaryAsync(item.Id, stoppingToken, item.ModelOverride);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to process period summary {Id}.", item.Id);
                    await _eventLogger.LogErrorAsync(Summary,
                        $"Period summary #{item.Id} failed: {ex.Message}",
                        source: nameof(PeriodSummaryService), relatedEntityId: item.Id,
                        relatedEntityType: "PeriodSummary", detail: ex.ToString());
                    await SetFailedAsync(item.Id, ex.Message, stoppingToken);
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

            var unfinished = await db.PeriodSummaries
                .Where(s => s.Status == "pending" || s.Status == "processing")
                .Select(s => s.Id)
                .ToListAsync(ct);

            foreach (var id in unfinished)
            {
                _queue.Enqueue((id, null));
                _signal.Release();
            }

            if (unfinished.Count > 0)
                _logger.LogInformation("Re-queued {Count} unfinished period summary(s).", unfinished.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-queue unfinished period summaries on startup.");
        }
    }

    private async Task SetFailedAsync(int summaryId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
            var summary = await db.PeriodSummaries.FindAsync(new object[] { summaryId }, ct);
            if (summary != null)
            {
                summary.Status = "failed";
                summary.ErrorMessage = error.Length > 2000 ? error[..2000] : error;
                summary.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            await _notifications.NotifyPeriodSummariesUpdatedAsync(1, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not mark period summary {Id} as failed.", summaryId);
        }
    }

    private async Task ProcessSummaryAsync(int summaryId, CancellationToken ct, string? modelOverride = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var timeZoneConverter = scope.ServiceProvider.GetRequiredService<TimeZoneConverter>();
        var gptClient = scope.ServiceProvider.GetRequiredService<IGptClient>();

        var summary = await db.PeriodSummaries.FindAsync(new object[] { summaryId }, ct);
        if (summary == null)
        {
            _logger.LogWarning("Period summary {Id} not found.", summaryId);
            return;
        }

        summary.Status = "processing";
        await db.SaveChangesAsync(ct);
        await _notifications.NotifyPeriodSummariesUpdatedAsync(1, ct);

        var analysisSettings = await settingsService.GetAnalysisSettingsAsync();
        var tz = timeZoneConverter.Resolve(analysisSettings.TimeZone);
        summary.TimeZone = analysisSettings.TimeZone;

        // ── Gather data ──────────────────────────────────────
        var readings = await db.GlucoseReadings
            .Where(r => r.Timestamp >= summary.PeriodStart && r.Timestamp < summary.PeriodEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var events = await db.GlucoseEvents
            .Where(e => e.EventTimestamp >= summary.PeriodStart && e.EventTimestamp < summary.PeriodEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        var stats = GlucoseStatsCalculator.ComputeDayStats(readings);

        summary.ReadingCount = stats.ReadingCount;
        summary.GlucoseMin = stats.Min;
        summary.GlucoseMax = stats.Max;
        summary.GlucoseAvg = stats.Avg;
        summary.GlucoseStdDev = stats.StdDev;
        summary.TimeInRange = stats.TimeInRange;
        summary.TimeAboveRange = stats.TimeAboveRange;
        summary.TimeBelowRange = stats.TimeBelowRange;
        summary.EventCount = events.Count;
        summary.EventIds = string.Join(",", events.Select(e => e.Id));
        summary.EventTitles = string.Join(" | ", events.Select(e => e.NoteTitle));

        await db.SaveChangesAsync(ct);

        // ── AI analysis ──────────────────────────────────────
        if (!analysisSettings.IsConfigured || string.IsNullOrWhiteSpace(analysisSettings.GptApiKey))
        {
            _logger.LogWarning("GPT API key not configured. Saving period summary without AI analysis.");
            summary.Status = "completed";
            summary.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await _notifications.NotifyPeriodSummariesUpdatedAsync(1, ct);
            return;
        }

        var (systemPrompt, userPrompt) = BuildPrompts(summary, readings, events, tz);

        var modelName = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride : analysisSettings.GptModelName;
        var gptResult = await gptClient.AnalyzeAsync(
            analysisSettings.GptApiKey, systemPrompt, userPrompt, modelName, 4096, ct);

        // Log AI usage
        await LogUsageAsync(db, summary, gptResult, ct);

        if (gptResult.Success && !string.IsNullOrWhiteSpace(gptResult.Content))
        {
            var (cleanAnalysis, classification) = ClassificationParser.Parse(gptResult.Content);
            summary.AiAnalysis = cleanAnalysis;
            summary.AiClassification = classification;
            summary.AiModel = gptResult.Model ?? modelName;
        }

        summary.Status = "completed";
        summary.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Period summary {Id} completed. {Readings} readings, {Events} events over {Duration}.",
            summary.Id, readings.Count, events.Count,
            FormatDuration(summary.PeriodEnd - summary.PeriodStart));

        var sumCost = AiCostCalculator.ComputeCost(gptResult.Model ?? modelName, gptResult.InputTokens, gptResult.OutputTokens);
        await _eventLogger.LogInfoAsync(Summary,
            $"Period summary #{summary.Id} completed{(!string.IsNullOrWhiteSpace(summary.Name) ? $": {summary.Name}" : "")}. " +
            $"{readings.Count} readings, {events.Count} events. Tokens: {gptResult.TotalTokens}, cost: ${sumCost:F4}.",
            source: nameof(PeriodSummaryService), relatedEntityId: summary.Id, relatedEntityType: "PeriodSummary",
            durationMs: gptResult.DurationMs);

        await _notifications.NotifyPeriodSummariesUpdatedAsync(1, ct);
        await _notifications.NotifyAiUsageUpdatedAsync(1, ct);
    }

    // ────────────────────────────────────────────────────────────
    // Prompt Building
    // ────────────────────────────────────────────────────────────

    private static (string systemPrompt, string userPrompt) BuildPrompts(
        PeriodSummary summary,
        List<GlucoseReading> readings,
        List<GlucoseEvent> events,
        TimeZoneInfo tz)
    {
        var sb = new StringBuilder();

        var localStart = TimeZoneConverter.ToLocal(summary.PeriodStart, tz);
        var localEnd = TimeZoneConverter.ToLocal(summary.PeriodEnd, tz);
        var duration = summary.PeriodEnd - summary.PeriodStart;

        sb.AppendLine($"=== PERIOD OVERVIEW ===");
        if (!string.IsNullOrWhiteSpace(summary.Name))
            sb.AppendLine($"Label: {summary.Name}");
        sb.AppendLine($"From: {localStart:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"To:   {localEnd:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Duration: {FormatDuration(duration)}");
        sb.AppendLine();

        sb.AppendLine("=== GLUCOSE STATISTICS ===");
        sb.AppendLine($"  Readings: {summary.ReadingCount}");
        if (summary.ReadingCount > 0)
        {
            sb.AppendLine($"  Glucose range: {summary.GlucoseMin} – {summary.GlucoseMax} mg/dL");
            sb.AppendLine($"  Average: {summary.GlucoseAvg} mg/dL");
            sb.AppendLine($"  Std deviation: {summary.GlucoseStdDev} mg/dL");
            sb.AppendLine($"  Time in range (70-180): {summary.TimeInRange}%");
            sb.AppendLine($"  Time above range (>180): {summary.TimeAboveRange}%");
            sb.AppendLine($"  Time below range (<70): {summary.TimeBelowRange}%");
        }
        sb.AppendLine();

        sb.AppendLine($"=== EVENTS ({events.Count}) ===");
        foreach (var evt in events)
        {
            var localTime = TimeZoneConverter.ToLocal(evt.EventTimestamp, tz);
            sb.AppendLine($"  • {localTime:yyyy-MM-dd HH:mm} — {evt.NoteTitle}");
            if (!string.IsNullOrWhiteSpace(evt.NoteContent))
                sb.AppendLine($"    Content: {Truncate(evt.NoteContent, 200)}");
            if (evt.GlucoseAtEvent.HasValue)
                sb.AppendLine($"    Glucose: {evt.GlucoseAtEvent} mg/dL, Spike: {(evt.GlucoseSpike.HasValue ? $"+{evt.GlucoseSpike}" : "N/A")} mg/dL");
            if (evt.AiClassification != null)
                sb.AppendLine($"    Classification: {evt.AiClassification}");
        }
        sb.AppendLine();

        sb.AppendLine("=== GLUCOSE TIMELINE (sampled) ===");
        if (readings.Count > 0)
        {
            var sample = readings.Count > 60
                ? readings.Where((_, i) => i % (readings.Count / 50 + 1) == 0).ToList()
                : readings;
            foreach (var r in sample)
            {
                sb.AppendLine($"  {TimeZoneConverter.ToLocal(r.Timestamp, tz):yyyy-MM-dd HH:mm} → {r.Value} mg/dL");
            }
        }

        var dataText = sb.ToString();

        var systemPrompt = $@"You are a diabetes management assistant analysing a USER-CHOSEN TIME PERIOD of glucose data.
The period may span hours, days, weeks, or months — adapt your analysis depth accordingly.

IMPORTANT: Your response MUST start with a classification line in this exact format:
[CLASSIFICATION: green]
or [CLASSIFICATION: yellow] or [CLASSIFICATION: red]

Classification guide:
- **green**: Good glucose control during this period. Time in range is high, few or mild spikes, stable patterns.
- **yellow**: Moderate control. Some concerning patterns, elevated variability, or occasional significant spikes, but not alarming overall.
- **red**: Poor control. Frequent or severe spikes, high time above range, low time in range, or dangerous lows.

After the classification line, your analysis should include these sections (use the exact bold headings):

1. **Overview**: High-level summary of glucose control during this period. Mention the duration, number of readings, and overall assessment.
2. **Key Metrics**: Summarize average glucose, time in range, variability (std dev), min/max, and time above/below range. How do these numbers compare to healthy targets?
3. **Glucose Patterns**: Describe patterns you observe — overnight stability, post-meal spikes, fasting levels, time-of-day trends, day-to-day consistency.
4. **Event Analysis**: Analyze each event (meal, activity, etc.) and its impact on glucose. Which events caused the biggest spikes? Which had minimal impact? Any patterns in event timing or type?
5. **Night & Morning Analysis**: Assess overnight glucose stability and fasting morning levels. Is there evidence of dawn phenomenon? Any concerning overnight trends?
6. **Actionable Insights**: Provide 3-5 specific, practical recommendations based on the data. Reference specific events or patterns when possible.

Keep the analysis concise but insightful. Use markdown formatting.
Do not include a title heading. Use mg/dL units. All timestamps are in the user's local time.
Write in a friendly, supportive tone — like a knowledgeable health coach.
If the period is very short (a few hours), focus on the specific events and glucose response during that window rather than long-term patterns.
If the period is long (weeks+), focus more on trends, averages, and high-level patterns rather than individual readings.";

        var userPrompt = $@"Please analyse this glucose monitoring period:
{(!string.IsNullOrWhiteSpace(summary.Name) ? $"\nPeriod label: {summary.Name}" : "")}

{dataText}";

        return (systemPrompt, userPrompt);
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
        GlucoseDbContext db, PeriodSummary summary, GptAnalysisResult result, CancellationToken ct)
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
                Reason = $"Period summary #{summary.Id}" + (!string.IsNullOrWhiteSpace(summary.Name) ? $": {summary.Name}" : ""),
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
            _logger.LogWarning(ex, "Failed to log AI usage for period summary {Id}.", summary.Id);
        }
    }
}
