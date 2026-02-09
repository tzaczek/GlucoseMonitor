using System.Text;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that generates daily summaries.
/// For each calendar day (in the user's timezone) that has glucose data or events,
/// it aggregates all events, pulls full-day glucose readings, computes day-level stats,
/// and calls GPT for a comprehensive daily analysis.
/// </summary>
public class DailySummaryService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailySummaryService> _logger;
    private readonly INotificationService _notifications;

    public DailySummaryService(
        IServiceProvider serviceProvider,
        ILogger<DailySummaryService> logger,
        INotificationService notifications)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailySummaryService started.");

        // Wait for other services to initialise
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await GenerateSummariesAsync(stoppingToken, includeToday: false, trigger: "auto");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in DailySummaryService.");
            }

            // Run every 30 minutes
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    /// <summary>
    /// Manually trigger daily summary generation for all days (including today).
    /// Can be called from the API controller.
    /// </summary>
    public async Task<int> TriggerGenerationAsync(CancellationToken ct)
    {
        _logger.LogInformation("Manual daily summary generation triggered.");
        return await GenerateSummariesAsync(ct, includeToday: true, trigger: "manual");
    }

    private async Task<int> GenerateSummariesAsync(CancellationToken ct, bool includeToday, string trigger = "auto")
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var timeZoneConverter = scope.ServiceProvider.GetRequiredService<TimeZoneConverter>();
        var gptClient = scope.ServiceProvider.GetRequiredService<IGptClient>();

        var analysisSettings = await settingsService.GetAnalysisSettingsAsync();
        if (!analysisSettings.IsConfigured || string.IsNullOrWhiteSpace(analysisSettings.GptApiKey))
        {
            _logger.LogDebug("GPT API key not configured. Skipping daily summary generation.");
            return 0;
        }

        var tz = timeZoneConverter.Resolve(analysisSettings.TimeZone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        // Find the range of days that have glucose readings
        var earliestReading = await db.GlucoseReadings
            .OrderBy(r => r.Timestamp)
            .Select(r => r.Timestamp)
            .FirstOrDefaultAsync(ct);

        if (earliestReading == default)
        {
            _logger.LogDebug("No glucose readings found. Skipping daily summary generation.");
            return 0;
        }

        var earliestLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(earliestReading, DateTimeKind.Utc), tz);
        var startDate = earliestLocal.Date;

        // Include today when manually triggered, otherwise only completed days
        var endDate = includeToday ? now.Date : now.Date.AddDays(-1);

        if (startDate > endDate)
        {
            _logger.LogDebug("No days to summarize yet.");
            return 0;
        }

        // Load existing processed summaries
        var existingSummaries = await db.DailySummaries
            .Where(s => s.IsProcessed)
            .Select(s => new { s.Date, s.PeriodEndUtc })
            .ToListAsync(ct);

        var existingDateSet = new HashSet<DateTime>(existingSummaries.Select(s => s.Date.Date));

        // Detect past days that were generated with partial data.
        // A summary is "partial" if its PeriodEndUtc is earlier than the actual end-of-day boundary,
        // meaning it was generated while the day was still in progress (e.g. triggered as "today").
        // This runs for both automatic and manual triggers so that partial days are always
        // regenerated once the full day's data is available.
        var partialDates = new HashSet<DateTime>();
        foreach (var s in existingSummaries)
        {
            var date = s.Date.Date;
            if (date >= now.Date) continue; // today is handled separately; skip future dates too

            var (_, fullDayEndUtc) = TimeZoneConverter.GetDayBoundariesUtc(date, tz);

            // If the stored PeriodEndUtc is more than 5 minutes before the real end-of-day,
            // this summary was generated from incomplete data and should be regenerated.
            if (s.PeriodEndUtc < fullDayEndUtc.AddMinutes(-5))
            {
                partialDates.Add(date);
            }
        }

        if (partialDates.Count > 0)
        {
            _logger.LogInformation("Found {Count} previously partial day(s) to regenerate: {Dates}",
                partialDates.Count, string.Join(", ", partialDates.OrderBy(d => d).Select(d => d.ToString("yyyy-MM-dd"))));
        }

        // Find days that need summaries
        var daysToProcess = new List<DateTime>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (includeToday && date == now.Date)
            {
                // Always regenerate today when manually triggered
                daysToProcess.Add(date);
            }
            else if (!existingDateSet.Contains(date))
            {
                // Day has never been processed
                daysToProcess.Add(date);
            }
            else if (partialDates.Contains(date))
            {
                // Day was previously generated with partial data — regenerate with full day
                daysToProcess.Add(date);
            }
        }

        if (daysToProcess.Count == 0)
        {
            _logger.LogDebug("All days have summaries. Nothing to do.");
            return 0;
        }

        _logger.LogInformation("Found {Count} day(s) needing summaries. Processing...", daysToProcess.Count);

        var processedCount = 0;
        foreach (var date in daysToProcess)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await GenerateDailySummaryAsync(db, gptClient, analysisSettings, tz, date, trigger, ct);
                processedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to generate daily summary for {Date}.", date.ToString("yyyy-MM-dd"));
            }

            // Rate limiting between API calls
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return processedCount;
    }

    private async Task GenerateDailySummaryAsync(
        GlucoseDbContext db,
        IGptClient gptClient,
        AnalysisSettingsDto analysisSettings,
        TimeZoneInfo tz,
        DateTime localDate,
        string trigger,
        CancellationToken ct)
    {
        // Convert local midnight boundaries to UTC (domain logic)
        var (periodStartUtc, periodEndUtc) = TimeZoneConverter.GetDayBoundariesUtc(localDate, tz);

        // For the current day, cap the period end at now (we only have data up to now)
        var nowUtc = DateTime.UtcNow;
        var effectivePeriodEndUtc = periodEndUtc > nowUtc ? nowUtc : periodEndUtc;

        // Get all glucose readings for this day (up to now if today)
        var readings = await db.GlucoseReadings
            .Where(r => r.Timestamp >= periodStartUtc && r.Timestamp < effectivePeriodEndUtc)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        // Get all events for this day (up to now if today)
        var events = await db.GlucoseEvents
            .Where(e => e.EventTimestamp >= periodStartUtc && e.EventTimestamp < effectivePeriodEndUtc)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        if (readings.Count == 0 && events.Count == 0)
        {
            _logger.LogDebug("No data for {Date}. Skipping.", localDate.ToString("yyyy-MM-dd"));
            return;
        }

        // Compute day-level glucose stats using domain service
        var dayStats = GlucoseStatsCalculator.ComputeDayStats(readings);

        var eventIds = string.Join(",", events.Select(e => e.Id));
        var eventTitles = string.Join(" | ", events.Select(e => e.NoteTitle));

        // Check if a summary row already exists
        var existing = await db.DailySummaries
            .FirstOrDefaultAsync(s => s.Date == localDate, ct);

        var summary = existing ?? new DailySummary();
        summary.Date = localDate;
        summary.PeriodStartUtc = periodStartUtc;
        summary.PeriodEndUtc = effectivePeriodEndUtc;
        summary.TimeZone = analysisSettings.TimeZone;
        summary.EventCount = events.Count;
        summary.EventIds = eventIds;
        summary.EventTitles = eventTitles;
        summary.ReadingCount = dayStats.ReadingCount;
        summary.GlucoseMin = dayStats.Min;
        summary.GlucoseMax = dayStats.Max;
        summary.GlucoseAvg = dayStats.Avg;
        summary.GlucoseStdDev = dayStats.StdDev;
        summary.TimeInRange = dayStats.TimeInRange;
        summary.TimeAboveRange = dayStats.TimeAboveRange;
        summary.TimeBelowRange = dayStats.TimeBelowRange;
        summary.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            summary.CreatedAt = DateTime.UtcNow;
            db.DailySummaries.Add(summary);
        }

        await db.SaveChangesAsync(ct);

        // Build prompts and call AI
        var isToday = localDate == TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz).Date;
        var (systemPrompt, userPrompt) = BuildDailySummaryPrompts(summary, events, readings, tz, isToday);

        const string modelName = "gpt-5-mini";
        var gptResult = await gptClient.AnalyzeAsync(
            analysisSettings.GptApiKey, systemPrompt, userPrompt, modelName, 4096, ct);

        // Log AI usage
        await LogUsageAsync(db, summary, gptResult, ct);

        if (gptResult.Success && !string.IsNullOrWhiteSpace(gptResult.Content))
        {
            // Parse classification from AI response (domain logic)
            var (cleanAnalysis, classification) = ClassificationParser.Parse(gptResult.Content);

            summary.AiAnalysis = cleanAnalysis;
            summary.AiClassification = classification;
            summary.IsProcessed = true;
            summary.ProcessedAt = DateTime.UtcNow;
            summary.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Save snapshot to history
            var snapshot = new DailySummarySnapshot
            {
                DailySummaryId = summary.Id,
                Date = localDate,
                GeneratedAt = DateTime.UtcNow,
                Trigger = trigger,
                DataStartUtc = periodStartUtc,
                DataEndUtc = effectivePeriodEndUtc,
                FirstReadingUtc = dayStats.FirstReadingUtc,
                LastReadingUtc = dayStats.LastReadingUtc,
                TimeZone = analysisSettings.TimeZone,
                EventCount = events.Count,
                EventIds = eventIds,
                EventTitles = eventTitles,
                ReadingCount = dayStats.ReadingCount,
                GlucoseMin = dayStats.Min,
                GlucoseMax = dayStats.Max,
                GlucoseAvg = dayStats.Avg,
                GlucoseStdDev = dayStats.StdDev,
                TimeInRange = dayStats.TimeInRange,
                TimeAboveRange = dayStats.TimeAboveRange,
                TimeBelowRange = dayStats.TimeBelowRange,
                AiAnalysis = cleanAnalysis,
                AiClassification = classification,
                IsProcessed = true
            };
            db.DailySummarySnapshots.Add(snapshot);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Daily summary generated for {Date} (trigger={Trigger}). Events: {EventCount}, Readings: {ReadingCount}.",
                localDate.ToString("yyyy-MM-dd"), trigger, events.Count, readings.Count);

            // Notify UI
            await _notifications.NotifyDailySummariesUpdatedAsync(1, ct);
        }
        else
        {
            _logger.LogWarning("AI returned empty analysis for daily summary {Date}.", localDate.ToString("yyyy-MM-dd"));
        }
    }

    // ────────────────────────────────────────────────────────────
    // Prompt Building
    // ────────────────────────────────────────────────────────────

    private static (string systemPrompt, string userPrompt) BuildDailySummaryPrompts(
        DailySummary summary,
        List<GlucoseEvent> events,
        List<GlucoseReading> readings,
        TimeZoneInfo tz,
        bool isPartialDay)
    {
        var sb = new StringBuilder();

        // Day overview
        sb.AppendLine($"=== DAY OVERVIEW: {summary.Date:yyyy-MM-dd} ({summary.Date:dddd}) ===");
        if (isPartialDay)
        {
            var lastTime = readings.Count > 0 ? TimeZoneConverter.ToLocal(readings.Last().Timestamp, tz) : (DateTime?)null;
            sb.AppendLine($"⚠️ PARTIAL DAY — data available up to {lastTime?.ToString("HH:mm") ?? "N/A"} (day still in progress)");
        }
        sb.AppendLine($"Total glucose readings: {readings.Count}");
        if (readings.Count > 0)
        {
            sb.AppendLine($"Glucose range: {summary.GlucoseMin} – {summary.GlucoseMax} mg/dL");
            sb.AppendLine($"Average: {summary.GlucoseAvg} mg/dL");
            sb.AppendLine($"Std deviation: {summary.GlucoseStdDev} mg/dL");
            sb.AppendLine($"Time in range (70-180): {summary.TimeInRange}%");
            sb.AppendLine($"Time above range (>180): {summary.TimeAboveRange}%");
            sb.AppendLine($"Time below range (<70): {summary.TimeBelowRange}%");
        }
        sb.AppendLine();

        // Events summary
        sb.AppendLine($"=== EVENTS ({events.Count} total) ===");
        if (events.Count == 0)
        {
            sb.AppendLine("No meal/activity events logged this day.");
        }
        else
        {
            foreach (var evt in events)
            {
                var localTime = TimeZoneConverter.ToLocal(evt.EventTimestamp, tz);
                sb.AppendLine($"--- {localTime:HH:mm} — {evt.NoteTitle} ---");
                if (!string.IsNullOrWhiteSpace(evt.NoteContent))
                    sb.AppendLine($"  Content: {evt.NoteContent}");
                if (evt.GlucoseAtEvent.HasValue)
                    sb.AppendLine($"  Glucose at event: {evt.GlucoseAtEvent} mg/dL");
                if (evt.GlucoseSpike.HasValue)
                    sb.AppendLine($"  Spike: +{evt.GlucoseSpike} mg/dL");
                if (evt.GlucoseMin.HasValue && evt.GlucoseMax.HasValue)
                    sb.AppendLine($"  Range during period: {evt.GlucoseMin} – {evt.GlucoseMax} mg/dL");
                if (!string.IsNullOrWhiteSpace(evt.AiAnalysis))
                    sb.AppendLine($"  Individual analysis: {Truncate(evt.AiAnalysis, 300)}");
                sb.AppendLine();
            }
        }

        // Hourly glucose profile
        sb.AppendLine("=== HOURLY GLUCOSE PROFILE ===");
        if (readings.Count > 0)
        {
            var hourlyGroups = readings
                .GroupBy(r => TimeZoneConverter.ToLocal(r.Timestamp, tz).Hour)
                .OrderBy(g => g.Key);

            foreach (var group in hourlyGroups)
            {
                var hourReadings = group.ToList();
                var avg = Math.Round(hourReadings.Average(r => r.Value), 1);
                var min = hourReadings.Min(r => r.Value);
                var max = hourReadings.Max(r => r.Value);
                sb.AppendLine($"  {group.Key:D2}:00 → avg {avg}, range {min}–{max} mg/dL ({hourReadings.Count} readings)");
            }
        }
        sb.AppendLine();

        // Full reading timeline (sampled if too many)
        sb.AppendLine("=== GLUCOSE TIMELINE ===");
        var sample = readings.Count > 60
            ? readings.Where((r, i) => i % (readings.Count / 50 + 1) == 0).ToList()
            : readings;
        foreach (var r in sample)
        {
            sb.AppendLine($"  {TimeZoneConverter.ToLocal(r.Timestamp, tz):HH:mm} → {r.Value} mg/dL");
        }

        var dataText = sb.ToString();

        var systemPrompt = @"You are a diabetes management assistant generating a comprehensive DAILY SUMMARY.
You are analyzing continuous glucose monitoring data along with all logged meals and activities.

NOTE: The data may be for a PARTIAL day (still in progress). If so, note this and base your analysis on the data available so far.

IMPORTANT: Your response MUST start with a classification line in this exact format:
[CLASSIFICATION: green]
or [CLASSIFICATION: yellow] or [CLASSIFICATION: red]

Classification guide for the overall day:
- **green**: Good day. Time in range ≥70%, no significant spikes (>60 mg/dL), stable glucose, no hypoglycemia.
- **yellow**: Concerning day. Time in range 50-70%, some notable spikes, moderate variability, or brief periods out of range.
- **red**: Difficult day. Time in range <50%, significant spikes, high variability, hypoglycemia episodes, or extended time above range.

After the classification line, your daily summary should include:
1. **Day Overview**: Overall glucose control assessment for the day. Was it a good, moderate, or difficult day?
2. **Key Metrics**: Comment on time in range, average glucose, and variability (standard deviation).
3. **Meal/Activity Impacts**: Summarize how each logged event affected glucose levels. Which meals caused the biggest spikes?
4. **Patterns & Trends**: Note any patterns — overnight trends, dawn phenomenon, post-meal patterns, afternoon dips, etc.
5. **Best & Worst Moments**: Identify the best-controlled period and the most challenging period of the day.
6. **Actionable Insights**: 2-3 specific, practical suggestions for improving glucose control based on the day's data.

Keep the summary concise but comprehensive (3-5 paragraphs). Use markdown formatting.
Do not include a title heading. Use mg/dL units. All timestamps are in the user's local time.
Write in a friendly, supportive tone — like a knowledgeable health coach.";

        var userPrompt = $@"Please provide a daily summary analysis for this day's glucose data:

{dataText}";

        return (systemPrompt, userPrompt);
    }

    // ────────────────────────────────────────────────────────────
    // AI Usage Logging
    // ────────────────────────────────────────────────────────────

    private async Task LogUsageAsync(
        GlucoseDbContext db, DailySummary summary, GptAnalysisResult result, CancellationToken ct)
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
                Reason = $"Daily summary for {summary.Date:yyyy-MM-dd}",
                Success = result.Success,
                HttpStatusCode = result.HttpStatusCode,
                FinishReason = result.FinishReason,
                CalledAt = DateTime.UtcNow,
                DurationMs = result.DurationMs
            });
            await db.SaveChangesAsync(ct);

            // Notify UI about new AI usage data
            await _notifications.NotifyAiUsageUpdatedAsync(1, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log AI usage for daily summary.");
        }
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "…";
    }
}
