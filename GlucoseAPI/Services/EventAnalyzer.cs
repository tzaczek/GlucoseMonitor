using System.Text;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using static GlucoseAPI.Application.Interfaces.EventCategory;

namespace GlucoseAPI.Services;

/// <summary>
/// Application service that runs AI analysis on a single GlucoseEvent.
/// Orchestrates domain services (stats, classification) and infrastructure (GPT client, notifications, DB).
/// Can be called from controllers (immediate) or from background services.
/// </summary>
public class EventAnalyzer
{
    private readonly GlucoseDbContext _db;
    private readonly SettingsService _settingsService;
    private readonly IGptClient _gptClient;
    private readonly INotificationService _notifications;
    private readonly TimeZoneConverter _timeZoneConverter;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<EventAnalyzer> _logger;

    public EventAnalyzer(
        GlucoseDbContext db,
        SettingsService settingsService,
        IGptClient gptClient,
        INotificationService notifications,
        TimeZoneConverter timeZoneConverter,
        IEventLogger eventLogger,
        ILogger<EventAnalyzer> logger)
    {
        _db = db;
        _settingsService = settingsService;
        _gptClient = gptClient;
        _notifications = notifications;
        _timeZoneConverter = timeZoneConverter;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    /// <summary>
    /// Run AI analysis on a single event. Returns the analysis text, or null on failure.
    /// Saves history entry and updates event in the database.
    /// </summary>
    /// <param name="evt">The event to analyze.</param>
    /// <param name="reason">Human-readable reason for the analysis run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="modelOverride">If set, uses this model instead of the one from settings (one-off override).</param>
    public async Task<string?> AnalyzeEventAsync(GlucoseEvent evt, string reason, CancellationToken ct = default, string? modelOverride = null)
    {
        var analysisSettings = await _settingsService.GetAnalysisSettingsAsync();

        if (!analysisSettings.IsConfigured || string.IsNullOrWhiteSpace(analysisSettings.GptApiKey))
        {
            _logger.LogWarning("GPT API key not configured. Cannot run analysis.");
            return null;
        }

        var tz = _timeZoneConverter.Resolve(analysisSettings.TimeZone);

        // Get glucose readings for this event's period
        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= evt.PeriodStart && r.Timestamp <= evt.PeriodEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        // Find other events whose timestamps fall within this event's glucose window.
        // These overlapping events may influence the glucose response and the AI should know about them.
        var overlappingEvents = await _db.GlucoseEvents
            .Where(e => e.Id != evt.Id
                && e.EventTimestamp >= evt.PeriodStart
                && e.EventTimestamp <= evt.PeriodEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        // Build prompts
        var (systemPrompt, userPrompt) = BuildEventPrompts(evt, readings, overlappingEvents, tz);
        var modelName = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride : analysisSettings.GptModelName;

        // Call GPT via the abstracted client
        var gptResult = await _gptClient.AnalyzeAsync(
            analysisSettings.GptApiKey, systemPrompt, userPrompt, modelName, 4096, ct);

        // Log AI usage regardless of success
        LogUsage(evt.Id, gptResult, reason);

        if (!gptResult.Success || string.IsNullOrWhiteSpace(gptResult.Content))
        {
            _logger.LogWarning("GPT returned empty analysis for event '{Title}' (ID={Id}).", evt.NoteTitle, evt.Id);

            var failCost = AiCostCalculator.ComputeCost(gptResult.Model ?? modelName, gptResult.InputTokens, gptResult.OutputTokens);
            await _eventLogger.LogWarningAsync(Analysis,
                $"AI analysis failed for event '{evt.NoteTitle}' (#{evt.Id}). Tokens: {gptResult.TotalTokens}, cost: ${failCost:F4}. Reason: {reason}",
                source: nameof(EventAnalyzer), relatedEntityId: evt.Id, relatedEntityType: "GlucoseEvent",
                durationMs: gptResult.DurationMs);

            // Save any pending AI usage log and notify UI
            await _db.SaveChangesAsync(ct);
            await _notifications.NotifyAiUsageUpdatedAsync(1, ct);

            return null;
        }

        // Parse classification from AI response (domain logic)
        var (analysis, classification) = ClassificationParser.Parse(gptResult.Content);

        // Compute glucose stats snapshot (domain logic)
        var stats = GlucoseStatsCalculator.ComputeEventStats(readings, evt.EventTimestamp);

        // Save to history (never lost)
        var historyEntry = new EventAnalysisHistory
        {
            GlucoseEventId = evt.Id,
            AiAnalysis = analysis,
            AiClassification = classification,
            AiModel = gptResult.Model ?? modelName,
            AnalyzedAt = DateTime.UtcNow,
            PeriodStart = evt.PeriodStart,
            PeriodEnd = evt.PeriodEnd,
            ReadingCount = readings.Count,
            Reason = reason,
            GlucoseAtEvent = stats.GlucoseAtEvent,
            GlucoseMin = stats.Min,
            GlucoseMax = stats.Max,
            GlucoseAvg = stats.Avg,
            GlucoseSpike = stats.Spike,
            PeakTime = stats.PeakTime
        };
        _db.EventAnalysisHistory.Add(historyEntry);

        // Update the main event
        evt.ReadingCount = stats.ReadingCount;
        evt.GlucoseAtEvent = stats.GlucoseAtEvent;
        evt.GlucoseMin = stats.Min;
        evt.GlucoseMax = stats.Max;
        evt.GlucoseAvg = stats.Avg;
        evt.GlucoseSpike = stats.Spike;
        evt.PeakTime = stats.PeakTime;
        evt.AiAnalysis = analysis;
        evt.AiClassification = classification;
        evt.AiModel = gptResult.Model ?? modelName;
        evt.IsProcessed = true;
        evt.ProcessedAt = DateTime.UtcNow;
        evt.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("AI analysis complete for event '{Title}' (ID={Id}). Reason: {Reason}",
            evt.NoteTitle, evt.Id, reason);

        var cost = AiCostCalculator.ComputeCost(gptResult.Model ?? modelName, gptResult.InputTokens, gptResult.OutputTokens);
        await _eventLogger.LogInfoAsync(Analysis,
            $"AI analysis completed for event '{evt.NoteTitle}' (#{evt.Id}). Classification: {classification}. " +
            $"Tokens: {gptResult.TotalTokens} ({gptResult.InputTokens} in / {gptResult.OutputTokens} out), cost: ${cost:F4}. Reason: {reason}",
            source: nameof(EventAnalyzer), relatedEntityId: evt.Id, relatedEntityType: "GlucoseEvent",
            durationMs: gptResult.DurationMs);

        // Notify UI
        await _notifications.NotifyEventsUpdatedAsync(1, ct);
        await _notifications.NotifyAiUsageUpdatedAsync(1, ct);

        return analysis;
    }

    // ────────────────────────────────────────────────────────────
    // Prompt Building
    // ────────────────────────────────────────────────────────────

    private static (string systemPrompt, string userPrompt) BuildEventPrompts(
        GlucoseEvent evt, List<GlucoseReading> readings, List<GlucoseEvent> overlappingEvents, TimeZoneInfo tz)
    {
        var sb = new StringBuilder();

        var beforeReadings = readings.Where(r => r.Timestamp < evt.EventTimestamp).ToList();
        var afterReadings = readings.Where(r => r.Timestamp >= evt.EventTimestamp).ToList();

        sb.AppendLine("=== GLUCOSE DATA BEFORE EVENT ===");
        if (beforeReadings.Count > 0)
        {
            sb.AppendLine($"Readings: {beforeReadings.Count}");
            sb.AppendLine($"Range: {beforeReadings.Min(r => r.Value)} – {beforeReadings.Max(r => r.Value)} mg/dL");
            sb.AppendLine($"Average: {Math.Round(beforeReadings.Average(r => r.Value), 1)} mg/dL");
            sb.AppendLine($"Last reading before event: {beforeReadings.Last().Value} mg/dL at {TimeZoneConverter.ToLocal(beforeReadings.Last().Timestamp, tz):HH:mm}");
            sb.AppendLine("Recent readings before:");
            foreach (var r in beforeReadings.TakeLast(10))
                sb.AppendLine($"  {TimeZoneConverter.ToLocal(r.Timestamp, tz):yyyy-MM-dd HH:mm} → {r.Value} mg/dL");
        }
        else
        {
            sb.AppendLine("No glucose readings before the event.");
        }

        sb.AppendLine();
        sb.AppendLine("=== GLUCOSE DATA AFTER EVENT ===");
        if (afterReadings.Count > 0)
        {
            sb.AppendLine($"Readings: {afterReadings.Count}");
            sb.AppendLine($"Range: {afterReadings.Min(r => r.Value)} – {afterReadings.Max(r => r.Value)} mg/dL");
            sb.AppendLine($"Average: {Math.Round(afterReadings.Average(r => r.Value), 1)} mg/dL");
            var peak = afterReadings.MaxBy(r => r.Value)!;
            sb.AppendLine($"Peak: {peak.Value} mg/dL at {TimeZoneConverter.ToLocal(peak.Timestamp, tz):HH:mm}");
            if (evt.GlucoseAtEvent.HasValue)
                sb.AppendLine($"Spike from baseline: +{Math.Round(peak.Value - evt.GlucoseAtEvent.Value, 1)} mg/dL");
            sb.AppendLine($"Time to peak: {(peak.Timestamp - evt.EventTimestamp).TotalMinutes:F0} minutes");

            sb.AppendLine("Readings after event:");
            foreach (var r in afterReadings.Take(15))
                sb.AppendLine($"  {TimeZoneConverter.ToLocal(r.Timestamp, tz):yyyy-MM-dd HH:mm} → {r.Value} mg/dL");
        }
        else
        {
            sb.AppendLine("No glucose readings after the event.");
        }

        // ── Overlapping events within the same glucose window ──
        if (overlappingEvents.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("=== OTHER EVENTS IN THIS GLUCOSE WINDOW ===");
            sb.AppendLine($"There are {overlappingEvents.Count} other event(s) within this event's glucose observation period.");
            sb.AppendLine("These may have influenced the glucose response you see above.");
            sb.AppendLine();

            foreach (var other in overlappingEvents)
            {
                var otherLocalTime = TimeZoneConverter.ToLocal(other.EventTimestamp, tz);
                var offset = other.EventTimestamp - evt.EventTimestamp;
                var direction = offset.TotalMinutes >= 0 ? "after" : "before";
                var absMinutes = Math.Abs(offset.TotalMinutes);

                sb.AppendLine($"  • \"{other.NoteTitle}\" at {otherLocalTime:HH:mm} ({absMinutes:F0} min {direction} this event)");
                if (!string.IsNullOrWhiteSpace(other.NoteContent))
                    sb.AppendLine($"    Content: {Truncate(other.NoteContent, 200)}");
                if (other.GlucoseAtEvent.HasValue)
                    sb.AppendLine($"    Glucose at that event: {other.GlucoseAtEvent.Value} mg/dL");
                if (other.AiClassification != null)
                    sb.AppendLine($"    Classification: {other.AiClassification}");
                sb.AppendLine();
            }
        }

        var glucoseDataText = sb.ToString();

        var systemPrompt = @"You are a diabetes management assistant analyzing glucose responses to food and activities. 
The user tracks their meals and activities in Samsung Notes (folder: sugar/food tracking).
Given a note describing what the user ate or did, plus glucose readings before and after, provide a clear and helpful analysis.

IMPORTANT: Your response MUST start with a classification line in this exact format:
[CLASSIFICATION: green]
or [CLASSIFICATION: yellow] or [CLASSIFICATION: red]

Classification guide:
- **green**: Glucose response was well-controlled. Spike ≤30 mg/dL, stayed in range (70-180), good recovery.
- **yellow**: Glucose response was concerning. Spike 30-60 mg/dL, briefly above range, or slow recovery.
- **red**: Glucose response was problematic. Spike >60 mg/dL, extended time above range, poor recovery, or hypoglycemia.

After the classification line, your analysis should include:
1. **Baseline Assessment**: What was the glucose level before the event?
2. **Glucose Response**: How did glucose levels change after the event? What was the peak and how long did it take?
3. **Spike Analysis**: Was this a significant spike? (Normal post-meal rise is 30-50 mg/dL; >60 mg/dL is notable)
4. **Recovery**: How long did it take for glucose to return toward baseline?
5. **Overall Assessment**: Was this a mild, moderate, or significant glucose impact?
6. **Practical Tip**: One actionable suggestion based on the data.

OVERLAPPING EVENTS:
If the data includes other events that occurred within the same glucose observation window, you MUST factor them into your analysis.
For example, exercise shortly after a meal can blunt a glucose spike, while a sugary drink before a meal can elevate the baseline.
- Mention the overlapping event(s) and explain how they likely influenced the glucose response.
- Attribute the glucose pattern to the combined effect rather than the main event alone when appropriate.
- If the overlapping event makes it difficult to isolate the main event's impact, state this clearly.

Keep the analysis concise (2-3 short paragraphs), practical, and written in a friendly tone.
Use mg/dL units. Format with markdown. Do not include a title heading.
If the note content is unclear, analyze based on the glucose patterns alone.
All timestamps below are in the user's local time.";

        var localEventTime = TimeZoneConverter.ToLocal(evt.EventTimestamp, tz);
        var userPrompt = $@"**Note Title:** {evt.NoteTitle}
**Note Content:** {evt.NoteContent ?? "(no text content)"}
**Event Time:** {localEventTime:yyyy-MM-dd HH:mm} (local time)
**Glucose at Event:** {(evt.GlucoseAtEvent.HasValue ? $"{evt.GlucoseAtEvent.Value} mg/dL" : "N/A")}

{glucoseDataText}

Please analyze this glucose response.";

        return (systemPrompt, userPrompt);
    }

    // ────────────────────────────────────────────────────────────
    // Text Helpers
    // ────────────────────────────────────────────────────────────

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength] + "…";
    }

    // ────────────────────────────────────────────────────────────
    // AI Usage Logging
    // ────────────────────────────────────────────────────────────

    /// <summary>Logs a GPT API call to the AiUsageLogs table.</summary>
    private void LogUsage(int? eventId, GptAnalysisResult result, string? reason)
    {
        try
        {
            _db.AiUsageLogs.Add(new AiUsageLog
            {
                GlucoseEventId = eventId,
                Model = result.Model,
                InputTokens = result.InputTokens,
                OutputTokens = result.OutputTokens,
                TotalTokens = result.TotalTokens,
                Reason = reason,
                Success = result.Success,
                HttpStatusCode = result.HttpStatusCode,
                FinishReason = result.FinishReason,
                CalledAt = DateTime.UtcNow,
                DurationMs = result.DurationMs
            });
            // SaveChanges is called by the caller (AnalyzeEventAsync)
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log AI usage.");
        }
    }
}
