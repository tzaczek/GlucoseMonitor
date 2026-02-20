using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using static GlucoseAPI.Application.Interfaces.EventCategory;

namespace GlucoseAPI.Services;

public class ChatService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ChatService> _logger;
    private readonly INotificationService _notifications;
    private readonly IEventLogger _eventLogger;
    private readonly ConcurrentQueue<(int MessageId, string? ModelOverride)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);

    public ChatService(
        IServiceProvider serviceProvider,
        ILogger<ChatService> logger,
        INotificationService notifications,
        IEventLogger eventLogger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
        _eventLogger = eventLogger;
    }

    public void Enqueue(int assistantMessageId, string? modelOverride = null)
    {
        _queue.Enqueue((assistantMessageId, modelOverride));
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ChatService started.");

        await RequeueUnfinishedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_queue.TryDequeue(out var item))
            {
                try
                {
                    await ProcessMessageAsync(item.MessageId, item.ModelOverride, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Failed to process chat message {Id}.", item.MessageId);
                    await SetFailedAsync(item.MessageId, ex.Message, stoppingToken);
                    await _eventLogger.LogErrorAsync(Chat,
                        $"Chat message #{item.MessageId} failed: {ex.Message}",
                        source: nameof(ChatService), relatedEntityId: item.MessageId,
                        relatedEntityType: "ChatMessage", detail: ex.ToString());
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

            var unfinished = await db.ChatMessages
                .Where(m => m.Role == "assistant" && m.Status == "processing")
                .Select(m => m.Id)
                .ToListAsync(ct);

            foreach (var id in unfinished)
            {
                _queue.Enqueue((id, null));
                _signal.Release();
            }

            if (unfinished.Count > 0)
                _logger.LogInformation("Re-queued {Count} unfinished chat message(s).", unfinished.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-queue unfinished chat messages.");
        }
    }

    private async Task ProcessMessageAsync(int assistantMessageId, string? modelOverride, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var timeZoneConverter = scope.ServiceProvider.GetRequiredService<TimeZoneConverter>();
        var gptClient = scope.ServiceProvider.GetRequiredService<IGptClient>();

        var assistantMsg = await db.ChatMessages
            .Include(m => m.Session)
            .FirstOrDefaultAsync(m => m.Id == assistantMessageId, ct);

        if (assistantMsg == null)
        {
            _logger.LogWarning("Chat message {Id} not found.", assistantMessageId);
            return;
        }

        var session = assistantMsg.Session;
        assistantMsg.Status = "processing";
        await db.SaveChangesAsync(ct);

        var analysisSettings = await settingsService.GetAnalysisSettingsAsync();
        if (!analysisSettings.IsConfigured || string.IsNullOrWhiteSpace(analysisSettings.GptApiKey))
        {
            assistantMsg.Content = "GPT API key is not configured. Please set it in Settings.";
            assistantMsg.Status = "failed";
            assistantMsg.ErrorMessage = "API key not configured";
            await db.SaveChangesAsync(ct);
            await NotifyCompletion(session.Id, assistantMsg.Id, ct);
            return;
        }

        var tz = timeZoneConverter.Resolve(analysisSettings.TimeZone);

        // Step 1: If there's a natural language period description but no dates, resolve it via AI
        if (!session.PeriodStart.HasValue && !session.PeriodEnd.HasValue
            && !string.IsNullOrWhiteSpace(session.PeriodDescription))
        {
            await ResolvePeriodFromDescriptionAsync(
                db, session, gptClient, analysisSettings.GptApiKey, modelOverride ?? analysisSettings.GptModelName, tz, ct);
        }

        // Load all prior messages for conversation context
        var allMessages = await db.ChatMessages
            .Where(m => m.ChatSessionId == session.Id && m.Id < assistantMessageId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        // Build glucose context from named periods or fallback to PeriodStart/PeriodEnd
        string glucoseContext = "";
        var periods = session.Periods;
        if (periods.Count > 0)
        {
            _logger.LogInformation("Building multi-period glucose context for session #{Id}: {Count} period(s)", session.Id, periods.Count);
            glucoseContext = await BuildMultiPeriodContextAsync(db, periods, tz, ct);
        }
        else if (session.PeriodStart.HasValue && session.PeriodEnd.HasValue)
        {
            _logger.LogInformation("Building glucose context for session #{Id}: {Start} to {End} (UTC)",
                session.Id, session.PeriodStart.Value, session.PeriodEnd.Value);
            glucoseContext = await BuildGlucoseContextAsync(db, session.PeriodStart.Value, session.PeriodEnd.Value, tz, ct);
        }

        if (string.IsNullOrWhiteSpace(glucoseContext))
            _logger.LogInformation("No glucose context for session #{Id}.", session.Id);
        else
            _logger.LogInformation("Glucose context built: {Len} chars for session #{Id}.", glucoseContext.Length, session.Id);

        // Resolve system prompt from template or default
        string systemPrompt;
        if (!string.IsNullOrWhiteSpace(session.TemplateName))
        {
            var template = await db.ChatPromptTemplates
                .FirstOrDefaultAsync(t => t.Name == session.TemplateName, ct);

            systemPrompt = template != null
                ? InterpolateTemplate(template.SystemPrompt, glucoseContext, session, tz)
                : BuildDefaultSystemPrompt(glucoseContext);
        }
        else
        {
            systemPrompt = BuildDefaultSystemPrompt(glucoseContext);
        }

        // Build the full user prompt: conversation history + latest user message
        var userPrompt = BuildConversationPrompt(allMessages, glucoseContext, session, tz);

        // Resolve model: override > settings default
        var modelName = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride : analysisSettings.GptModelName;

        var gptResult = await gptClient.AnalyzeAsync(
            analysisSettings.GptApiKey, systemPrompt, userPrompt, modelName, 4096, ct);

        // Log AI usage
        db.AiUsageLogs.Add(new AiUsageLog
        {
            Model = gptResult.Model ?? modelName,
            InputTokens = gptResult.InputTokens,
            OutputTokens = gptResult.OutputTokens,
            TotalTokens = gptResult.TotalTokens,
            Reason = $"Chat session #{session.Id}",
            Success = gptResult.Success,
            HttpStatusCode = gptResult.HttpStatusCode,
            FinishReason = gptResult.FinishReason,
            CalledAt = DateTime.UtcNow,
            DurationMs = gptResult.DurationMs,
        });

        if (gptResult.Success && !string.IsNullOrWhiteSpace(gptResult.Content))
        {
            assistantMsg.Content = gptResult.Content;
            assistantMsg.Status = "completed";
            assistantMsg.AiModel = gptResult.Model ?? modelName;
            assistantMsg.InputTokens = gptResult.InputTokens;
            assistantMsg.OutputTokens = gptResult.OutputTokens;
            assistantMsg.DurationMs = gptResult.DurationMs;
            assistantMsg.CostUsd = AiCostCalculator.ComputeCost(
                gptResult.Model ?? modelName, gptResult.InputTokens, gptResult.OutputTokens);
            assistantMsg.ReferencedEventIds = ExtractEventIds(gptResult.Content);
        }
        else
        {
            assistantMsg.Content = gptResult.ErrorMessage ?? "AI request failed.";
            assistantMsg.Status = "failed";
            assistantMsg.ErrorMessage = gptResult.ErrorMessage;
            assistantMsg.AiModel = gptResult.Model ?? modelName;
        }

        session.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var cost = assistantMsg.CostUsd ?? 0;
        await _eventLogger.LogInfoAsync(Chat,
            $"Chat response in session #{session.Id} '{session.Title}'. " +
            $"Tokens: {gptResult.TotalTokens} ({gptResult.InputTokens} in / {gptResult.OutputTokens} out), cost: ${cost:F4}.",
            source: nameof(ChatService), relatedEntityId: session.Id, relatedEntityType: "ChatSession",
            durationMs: gptResult.DurationMs);

        await NotifyCompletion(session.Id, assistantMsg.Id, ct);
    }

    private async Task NotifyCompletion(int sessionId, int messageId, CancellationToken ct)
    {
        await _notifications.NotifyChatMessageCompletedAsync(sessionId, messageId, ct);
        await _notifications.NotifyChatSessionsUpdatedAsync(1, ct);
        await _notifications.NotifyAiUsageUpdatedAsync(1, ct);
    }

    private async Task SetFailedAsync(int messageId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
            var msg = await db.ChatMessages
                .Include(m => m.Session)
                .FirstOrDefaultAsync(m => m.Id == messageId, ct);
            if (msg != null)
            {
                msg.Status = "failed";
                msg.Content = "An error occurred while processing your request.";
                msg.ErrorMessage = error.Length > 500 ? error[..500] : error;
                msg.Session.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await _notifications.NotifyChatMessageCompletedAsync(msg.ChatSessionId, msg.Id, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not mark chat message {Id} as failed.", messageId);
        }
    }

    // ── Period resolution from natural language ────────────────

    private async Task ResolvePeriodFromDescriptionAsync(
        GlucoseDbContext db, ChatSession session, IGptClient gptClient,
        string apiKey, string modelName, TimeZoneInfo tz, CancellationToken ct)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

        var systemPrompt = $@"You are a date/time extraction assistant. The current date and time is: {nowLocal:yyyy-MM-dd HH:mm} ({tz.Id}).

Extract the time period the user is referring to. Return ONLY a JSON object with exactly these fields:
- ""startDate"": ISO 8601 datetime string in the user's local timezone
- ""endDate"": ISO 8601 datetime string in the user's local timezone

Examples:
- ""last night"" → the previous night, typically 22:00 to 06:00
- ""yesterday morning"" → yesterday 06:00 to 12:00
- ""Monday evening"" → the most recent Monday 18:00 to 22:00
- ""last 3 days"" → 3 days ago at 00:00 to now
- ""February 8th"" → Feb 8 00:00 to Feb 9 00:00
- ""last week"" → 7 days ago 00:00 to today 00:00

Return ONLY the JSON, no markdown, no explanation.";

        var userPrompt = session.PeriodDescription!;

        _logger.LogInformation("Resolving period from description for session #{Id}: \"{Desc}\"",
            session.Id, session.PeriodDescription);

        // Use a fast/cheap model for date extraction if available, otherwise fall back
        var extractionModel = "gpt-4o-mini";
        var result = await gptClient.AnalyzeAsync(apiKey, systemPrompt, userPrompt, extractionModel, 256, ct);

        _logger.LogInformation(
            "Period resolution API result for session #{Id}: Success={Success}, HTTP={Http}, " +
            "Model={Model}, Tokens={Tokens}, ContentLength={Len}, FinishReason={Reason}",
            session.Id, result.Success, result.HttpStatusCode,
            result.Model, result.TotalTokens,
            result.Content?.Length ?? 0, result.FinishReason);

        db.AiUsageLogs.Add(new AiUsageLog
        {
            Model = result.Model ?? extractionModel,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            TotalTokens = result.TotalTokens,
            Reason = $"Chat period resolution #{session.Id}",
            Success = result.Success,
            HttpStatusCode = result.HttpStatusCode,
            FinishReason = result.FinishReason,
            CalledAt = DateTime.UtcNow,
            DurationMs = result.DurationMs,
        });

        if (!result.Success)
        {
            _logger.LogWarning("Period resolution API call failed for session #{Id}: HTTP {Http}, Error: {Error}",
                session.Id, result.HttpStatusCode, result.ErrorMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.Content))
        {
            _logger.LogWarning("Period resolution returned empty content for session #{Id}. FinishReason: {Reason}",
                session.Id, result.FinishReason);
            return;
        }

        _logger.LogInformation("Period resolution raw content for session #{Id}: {Content}",
            session.Id, result.Content);

        try
        {
            var json = result.Content.Trim();
            if (json.StartsWith("```"))
            {
                json = Regex.Replace(json, @"^```\w*\s*", "");
                json = Regex.Replace(json, @"\s*```$", "");
                json = json.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var startStr = root.GetProperty("startDate").GetString();
            var endStr = root.GetProperty("endDate").GetString();

            if (startStr != null && endStr != null)
            {
                var localStart = DateTime.Parse(startStr);
                var localEnd = DateTime.Parse(endStr);

                session.PeriodStart = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), tz);
                session.PeriodEnd = TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified), tz);
                session.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Period resolved for session #{Id}: \"{Desc}\" → {Start} to {End} (UTC)",
                    session.Id, session.PeriodDescription,
                    session.PeriodStart.Value, session.PeriodEnd.Value);

                var cost = AiCostCalculator.ComputeCost(
                    result.Model ?? extractionModel, result.InputTokens, result.OutputTokens);
                await _eventLogger.LogInfoAsync(Chat,
                    $"Period resolved for session #{session.Id}: \"{session.PeriodDescription}\" → " +
                    $"{localStart:yyyy-MM-dd HH:mm} to {localEnd:yyyy-MM-dd HH:mm}. Cost: ${cost:F4}.",
                    source: nameof(ChatService), relatedEntityId: session.Id,
                    relatedEntityType: "ChatSession", durationMs: result.DurationMs);

                await _notifications.NotifyChatPeriodResolvedAsync(
                    session.Id, session.PeriodStart.Value, session.PeriodEnd.Value, ct);
            }
            else
            {
                _logger.LogWarning("Period resolution JSON missing startDate/endDate for session #{Id}. JSON: {Json}",
                    session.Id, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse period resolution JSON for session #{Id}. Raw: {Content}",
                session.Id, result.Content);
        }
    }

    // ── Prompt building helpers ──────────────────────────────

    private static string BuildDefaultSystemPrompt(string glucoseContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine(@"You are a knowledgeable diabetes management assistant. The user is tracking their glucose levels with a continuous glucose monitor (CGM) and logging meals/activities.

You help them understand their glucose data, identify patterns, and make better decisions for glucose management.

Guidelines:
- Use mg/dL units for glucose values.
- All timestamps are in the user's local time.
- Be specific and reference actual data points when possible.
- When mentioning events (meals/activities), include their ID like this: event #123 — this creates clickable links.
- Format responses with markdown for readability.
- Be friendly and supportive, like a knowledgeable health coach.
- If you don't have enough data to answer, say so clearly.");

        if (!string.IsNullOrWhiteSpace(glucoseContext))
        {
            sb.AppendLine();
            sb.AppendLine("=== GLUCOSE DATA CONTEXT ===");
            sb.AppendLine(glucoseContext);
        }

        return sb.ToString();
    }

    private static string InterpolateTemplate(string templateText, string glucoseContext,
        ChatSession session, TimeZoneInfo tz)
    {
        var result = templateText;

        var periodLabel = "";
        var periods = session.Periods;
        if (periods.Count > 0)
        {
            var parts = periods.Select(p =>
            {
                var s = TimeZoneInfo.ConvertTimeFromUtc(p.Start, tz);
                var e = TimeZoneInfo.ConvertTimeFromUtc(p.End, tz);
                return $"\"{p.Name}\" ({s:yyyy-MM-dd HH:mm} to {e:yyyy-MM-dd HH:mm})";
            });
            periodLabel = string.Join(", ", parts);
        }
        else if (session.PeriodStart.HasValue && session.PeriodEnd.HasValue)
        {
            var start = TimeZoneInfo.ConvertTimeFromUtc(session.PeriodStart.Value, tz);
            var end = TimeZoneInfo.ConvertTimeFromUtc(session.PeriodEnd.Value, tz);
            periodLabel = $"{start:yyyy-MM-dd HH:mm} to {end:yyyy-MM-dd HH:mm}";
        }

        // Handle both single and double brace placeholders (DB may store either)
        foreach (var (placeholder, value) in new[]
        {
            ("glucose_data", glucoseContext),
            ("events", glucoseContext),
            ("period_label", periodLabel),
        })
        {
            result = result.Replace($"{{{{{placeholder}}}}}", value);  // {{placeholder}}
            result = result.Replace($"{{{placeholder}}}", value);       // {placeholder}
        }

        return result;
    }

    private static string BuildConversationPrompt(List<ChatMessage> priorMessages,
        string glucoseContext, ChatSession session, TimeZoneInfo tz)
    {
        var sb = new StringBuilder();

        // Include glucose context directly in user prompt so the model always sees it
        if (!string.IsNullOrWhiteSpace(glucoseContext))
        {
            sb.AppendLine("=== GLUCOSE AND EVENT DATA ===");
            sb.AppendLine(glucoseContext);
            sb.AppendLine("=== END GLUCOSE AND EVENT DATA ===");
            sb.AppendLine();
        }

        // Include conversation history
        if (priorMessages.Count > 1)
        {
            sb.AppendLine("=== CONVERSATION HISTORY ===");
            foreach (var msg in priorMessages.SkipLast(1))
            {
                var role = msg.Role == "user" ? "User" : "Assistant";
                sb.AppendLine($"[{role}]: {Truncate(msg.Content, 2000)}");
                sb.AppendLine();
            }
            sb.AppendLine("=== END CONVERSATION HISTORY ===");
            sb.AppendLine();
        }

        // The latest user message
        var lastUserMsg = priorMessages.LastOrDefault(m => m.Role == "user");
        if (lastUserMsg != null)
        {
            sb.AppendLine(lastUserMsg.Content);
        }

        return sb.ToString();
    }

    private static async Task<string> BuildMultiPeriodContextAsync(
        GlucoseDbContext db, List<ChatPeriod> periods, TimeZoneInfo tz, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"The user selected {periods.Count} period(s) for analysis. Each period has a name that may be referenced in the prompt.");
        sb.AppendLine();

        foreach (var period in periods)
        {
            sb.AppendLine($"=== PERIOD: \"{period.Name}\" ===");
            var periodContext = await BuildGlucoseContextAsync(db, period.Start, period.End, tz, ct);
            if (string.IsNullOrWhiteSpace(periodContext))
                sb.AppendLine("(No glucose data available for this period)");
            else
                sb.Append(periodContext);
            sb.AppendLine($"=== END PERIOD: \"{period.Name}\" ===");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static async Task<string> BuildGlucoseContextAsync(
        GlucoseDbContext db, DateTime periodStart, DateTime periodEnd, TimeZoneInfo tz, CancellationToken ct)
    {
        var readings = await db.GlucoseReadings
            .Where(r => r.Timestamp >= periodStart && r.Timestamp < periodEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var events = await db.GlucoseEvents
            .Where(e => e.EventTimestamp >= periodStart && e.EventTimestamp < periodEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        var sb = new StringBuilder();

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(periodStart, tz);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(periodEnd, tz);
        sb.AppendLine($"Period: {localStart:yyyy-MM-dd HH:mm} to {localEnd:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Readings: {readings.Count}");

        if (readings.Count > 0)
        {
            var stats = GlucoseStatsCalculator.ComputeDayStats(readings);
            sb.AppendLine($"Glucose range: {stats.Min} – {stats.Max} mg/dL");
            sb.AppendLine($"Average: {stats.Avg} mg/dL");
            sb.AppendLine($"Std deviation: {stats.StdDev} mg/dL");
            sb.AppendLine($"Time in range (70-180): {stats.TimeInRange}%");
            sb.AppendLine($"Time above range (>180): {stats.TimeAboveRange}%");
            sb.AppendLine($"Time below range (<70): {stats.TimeBelowRange}%");
        }
        sb.AppendLine();

        sb.AppendLine($"Events ({events.Count}):");
        foreach (var evt in events)
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(evt.EventTimestamp, tz);
            sb.AppendLine($"  - event #{evt.Id}: {localTime:yyyy-MM-dd HH:mm} — {evt.NoteTitle}");
            if (!string.IsNullOrWhiteSpace(evt.NoteContent))
                sb.AppendLine($"    Content: {Truncate(evt.NoteContent, 200)}");
            if (evt.GlucoseAtEvent.HasValue)
                sb.AppendLine($"    Glucose: {evt.GlucoseAtEvent} mg/dL, Spike: {(evt.GlucoseSpike.HasValue ? $"+{evt.GlucoseSpike}" : "N/A")} mg/dL");
            if (evt.AiClassification != null)
                sb.AppendLine($"    Classification: {evt.AiClassification}");
        }
        sb.AppendLine();

        // Sampled glucose timeline
        sb.AppendLine("Glucose Timeline (sampled):");
        if (readings.Count > 0)
        {
            var sample = readings.Count > 60
                ? readings.Where((_, i) => i % (readings.Count / 50 + 1) == 0).ToList()
                : readings;
            foreach (var r in sample)
            {
                sb.AppendLine($"  {TimeZoneInfo.ConvertTimeFromUtc(r.Timestamp, tz):yyyy-MM-dd HH:mm} → {r.Value} mg/dL");
            }
        }

        return sb.ToString();
    }

    private static string? ExtractEventIds(string content)
    {
        var matches = Regex.Matches(content, @"event\s*#(\d+)", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return null;

        var ids = matches.Select(m => m.Groups[1].Value).Distinct().ToList();
        var result = string.Join(",", ids);
        return result.Length > 500 ? result[..500] : result;
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
