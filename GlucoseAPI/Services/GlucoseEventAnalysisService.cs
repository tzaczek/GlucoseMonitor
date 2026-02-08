using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that correlates Samsung Notes (from a configurable folder, default "Cukier")
/// with glucose readings. For each note, it identifies glucose data before and after the event,
/// computes statistics, and calls the OpenAI GPT API for analysis via <see cref="EventAnalyzer"/>.
///
/// When a new event is created the **previous** event is automatically re-analysed because its
/// PeriodEnd boundary changes. Every analysis (initial and re-analysis) is saved to the
/// EventAnalysisHistory table so nothing is ever lost.
/// </summary>
public class GlucoseEventAnalysisService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GlucoseEventAnalysisService> _logger;
    private readonly INotificationService _notifications;

    // Default lookback/lookahead when there is no adjacent event
    private static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(3);
    private static readonly TimeSpan DefaultLookahead = TimeSpan.FromHours(4);

    public GlucoseEventAnalysisService(
        IServiceProvider serviceProvider,
        ILogger<GlucoseEventAnalysisService> logger,
        INotificationService notifications)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GlucoseEventAnalysisService started.");

        // Wait for other services to initialise
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            int intervalMinutes = 15;
            try
            {
                intervalMinutes = await GetIntervalMinutesAsync();
                await ProcessEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in GlucoseEventAnalysisService.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task<int> GetIntervalMinutesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var analysisSettings = await settings.GetAnalysisSettingsAsync();
        return Math.Max(1, analysisSettings.AnalysisIntervalMinutes);
    }

    // ────────────────────────────────────────────────────────────
    // Main processing loop
    // ────────────────────────────────────────────────────────────

    private async Task ProcessEventsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var analyzer = scope.ServiceProvider.GetRequiredService<EventAnalyzer>();

        var analysisSettings = await settingsService.GetAnalysisSettingsAsync();
        var folderName = analysisSettings.NotesFolderName;

        if (string.IsNullOrWhiteSpace(folderName))
        {
            _logger.LogDebug("Analysis folder name not configured. Skipping.");
            return;
        }

        // 1. Get all notes from the configured folder, ordered by modified time
        var folderNotes = await db.SamsungNotes
            .Where(n => !n.IsDeleted && n.FolderName == folderName)
            .OrderBy(n => n.ModifiedAt)
            .ToListAsync(ct);

        if (folderNotes.Count == 0)
        {
            _logger.LogDebug("No notes found in folder '{Folder}'. Skipping.", folderName);
            return;
        }

        // 2. Get existing events to skip already-processed ones
        var existingUuids = await db.GlucoseEvents
            .Select(e => e.NoteUuid)
            .ToListAsync(ct);

        var existingUuidSet = new HashSet<string>(existingUuids);

        // Find notes that haven't been turned into events yet
        var newNotes = folderNotes.Where(n => !existingUuidSet.Contains(n.Uuid)).ToList();

        if (newNotes.Count == 0)
        {
            // Check if any existing events still need AI analysis
            var unanalyzed = await db.GlucoseEvents
                .Where(e => !e.IsProcessed)
                .ToListAsync(ct);

            if (unanalyzed.Count > 0)
            {
                _logger.LogInformation("Found {Count} unanalyzed events. Attempting AI analysis...", unanalyzed.Count);

                // Distinguish initial analysis from re-analysis triggered by new glucose data
                var hasExistingAnalysis = await db.EventAnalysisHistory
                    .Where(h => unanalyzed.Select(e => e.Id).Contains(h.GlucoseEventId))
                    .Select(h => h.GlucoseEventId)
                    .Distinct()
                    .ToListAsync(ct);
                var reanalysisIds = new HashSet<int>(hasExistingAnalysis);

                // Apply cooldown: for re-analyses triggered by new glucose data,
                // skip events whose last analysis is within the configured cooldown window.
                var cooldownMinutes = Math.Max(1, analysisSettings.ReanalysisMinIntervalMinutes);
                var cooldownThreshold = DateTime.UtcNow.AddMinutes(-cooldownMinutes);

                var eventsToAnalyze = new List<GlucoseEvent>();
                foreach (var evt in unanalyzed)
                {
                    if (reanalysisIds.Contains(evt.Id))
                    {
                        // This is a re-analysis — check cooldown
                        var lastAnalysis = await db.EventAnalysisHistory
                            .Where(h => h.GlucoseEventId == evt.Id)
                            .OrderByDescending(h => h.AnalyzedAt)
                            .Select(h => h.AnalyzedAt)
                            .FirstOrDefaultAsync(ct);

                        if (lastAnalysis > cooldownThreshold)
                        {
                            _logger.LogDebug(
                                "Skipping re-analysis for event '{Title}' (ID={Id}) — last analysis was {Ago} min ago, cooldown is {Cooldown} min.",
                                evt.NoteTitle, evt.Id,
                                Math.Round((DateTime.UtcNow - lastAnalysis).TotalMinutes, 1),
                                cooldownMinutes);
                            continue;
                        }
                    }
                    eventsToAnalyze.Add(evt);
                }

                if (eventsToAnalyze.Count > 0)
                {
                    await RunBatchAnalysisAsync(analyzer, eventsToAnalyze, "Initial analysis", ct, reanalysisIds);
                }
                else
                {
                    _logger.LogDebug("All {Count} unanalyzed events are within the re-analysis cooldown window ({CooldownMin} min). Skipping.",
                        unanalyzed.Count, cooldownMinutes);
                }
            }
            else
            {
                _logger.LogDebug("All events are up to date. No new notes to process.");
            }
            return;
        }

        _logger.LogInformation("Processing {Count} new note(s) from folder '{Folder}'...", newNotes.Count, folderName);

        int created = 0;
        var eventsNeedingReanalysis = new List<GlucoseEvent>();

        foreach (var note in newNotes)
        {
            if (ct.IsCancellationRequested) break;

            var eventTimestamp = DateTime.SpecifyKind(note.ModifiedAt, DateTimeKind.Utc);

            // Calculate period boundaries based on adjacent notes
            var prevNote = folderNotes
                .Where(n => n.ModifiedAt < note.ModifiedAt)
                .OrderByDescending(n => n.ModifiedAt)
                .FirstOrDefault();

            var nextNote = folderNotes
                .Where(n => n.ModifiedAt > note.ModifiedAt)
                .OrderBy(n => n.ModifiedAt)
                .FirstOrDefault();

            var periodStart = prevNote != null
                ? DateTime.SpecifyKind(prevNote.ModifiedAt, DateTimeKind.Utc)
                : eventTimestamp - DefaultLookback;

            var periodEnd = nextNote != null
                ? DateTime.SpecifyKind(nextNote.ModifiedAt, DateTimeKind.Utc)
                : eventTimestamp + DefaultLookahead;

            // ── Re-analyse the previous event ──────────────────────
            if (prevNote != null)
            {
                var prevEvent = await db.GlucoseEvents
                    .FirstOrDefaultAsync(e => e.NoteUuid == prevNote.Uuid, ct);

                if (prevEvent != null)
                {
                    var newPeriodEnd = eventTimestamp;
                    if (prevEvent.PeriodEnd != newPeriodEnd)
                    {
                        _logger.LogInformation(
                            "Updating previous event '{Title}' (ID={Id}) PeriodEnd from {Old} → {New} due to new event.",
                            prevEvent.NoteTitle, prevEvent.Id, prevEvent.PeriodEnd, newPeriodEnd);

                        prevEvent.PeriodEnd = newPeriodEnd;

                        // Recompute glucose stats using domain service
                        await RecomputeGlucoseStatsAsync(db, prevEvent, ct);

                        prevEvent.IsProcessed = false;
                        prevEvent.UpdatedAt = DateTime.UtcNow;

                        eventsNeedingReanalysis.Add(prevEvent);
                    }
                }
            }

            // Get glucose readings for this period
            var readings = await db.GlucoseReadings
                .Where(r => r.Timestamp >= periodStart && r.Timestamp <= periodEnd)
                .OrderBy(r => r.Timestamp)
                .ToListAsync(ct);

            // Compute stats using domain service
            var stats = GlucoseStatsCalculator.ComputeEventStats(readings, eventTimestamp);

            var glucoseEvent = new GlucoseEvent
            {
                SamsungNoteId = note.Id,
                NoteUuid = note.Uuid,
                NoteTitle = note.Title,
                NoteContent = note.TextContent,
                EventTimestamp = eventTimestamp,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                ReadingCount = stats.ReadingCount,
                GlucoseAtEvent = stats.GlucoseAtEvent,
                GlucoseMin = stats.Min,
                GlucoseMax = stats.Max,
                GlucoseAvg = stats.Avg,
                GlucoseSpike = stats.Spike,
                PeakTime = stats.PeakTime,
                IsProcessed = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.GlucoseEvents.Add(glucoseEvent);
            created++;
        }

        if (created > 0 || eventsNeedingReanalysis.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Created {Created} glucose events. {Reanalysis} previous events queued for re-analysis.",
                created, eventsNeedingReanalysis.Count);

            // Run AI analysis for all unanalyzed events (new + re-analysis)
            var unanalyzed = await db.GlucoseEvents
                .Where(e => !e.IsProcessed)
                .OrderBy(e => e.EventTimestamp)
                .ToListAsync(ct);

            var reanalysisIds = new HashSet<int>(eventsNeedingReanalysis.Select(e => e.Id));

            await RunBatchAnalysisAsync(analyzer, unanalyzed, null, ct, reanalysisIds);

            // Notify UI
            await _notifications.NotifyEventsUpdatedAsync(created + eventsNeedingReanalysis.Count, ct);
        }
    }

    // ────────────────────────────────────────────────────────────
    // Recompute glucose stats for an event (after period change)
    // ────────────────────────────────────────────────────────────

    private static async Task RecomputeGlucoseStatsAsync(GlucoseDbContext db, GlucoseEvent evt, CancellationToken ct)
    {
        var readings = await db.GlucoseReadings
            .Where(r => r.Timestamp >= evt.PeriodStart && r.Timestamp <= evt.PeriodEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        // Use domain service for stats computation
        var stats = GlucoseStatsCalculator.ComputeEventStats(readings, evt.EventTimestamp);

        evt.ReadingCount = stats.ReadingCount;
        evt.GlucoseAtEvent = stats.GlucoseAtEvent;
        evt.GlucoseMin = stats.Min;
        evt.GlucoseMax = stats.Max;
        evt.GlucoseAvg = stats.Avg;
        evt.GlucoseSpike = stats.Spike;
        evt.PeakTime = stats.PeakTime;
    }

    // ────────────────────────────────────────────────────────────
    // Batch AI Analysis (delegates to EventAnalyzer per event)
    // ────────────────────────────────────────────────────────────

    private async Task RunBatchAnalysisAsync(
        EventAnalyzer analyzer,
        List<GlucoseEvent> events,
        string? defaultReason,
        CancellationToken ct,
        HashSet<int>? reanalysisIds = null)
    {
        foreach (var evt in events)
        {
            if (ct.IsCancellationRequested) break;

            bool isReanalysis = reanalysisIds != null && reanalysisIds.Contains(evt.Id);
            string reason = isReanalysis
                ? (defaultReason == "Initial analysis"
                    ? "Re-analysis: new glucose data received"
                    : "Re-analysis: period boundary changed due to new event")
                : (defaultReason ?? "Initial analysis");

            try
            {
                await analyzer.AnalyzeEventAsync(evt, reason, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed AI analysis for event '{Title}' (ID={Id}).", evt.NoteTitle, evt.Id);
            }

            // Rate limiting between API calls
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
