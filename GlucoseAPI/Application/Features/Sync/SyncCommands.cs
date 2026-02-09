using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.Sync;

// ── Shared result ─────────────────────────────────────────────

public record SyncSourceResult(string Source, bool Success, string Message);

// ── TriggerFullSync ───────────────────────────────────────────

public record TriggerFullSyncCommand : IRequest<TriggerFullSyncResult>;

public record TriggerFullSyncResult(bool AllSuccess, string Message, List<SyncSourceResult> Results);

public class TriggerFullSyncHandler : IRequestHandler<TriggerFullSyncCommand, TriggerFullSyncResult>
{
    private readonly GlucoseFetchService _glucoseFetch;
    private readonly SamsungNotesSyncService _notesSync;
    private readonly GlucoseEventAnalysisService _eventAnalysis;
    private readonly ILogger<TriggerFullSyncHandler> _logger;

    public TriggerFullSyncHandler(
        GlucoseFetchService glucoseFetch,
        SamsungNotesSyncService notesSync,
        GlucoseEventAnalysisService eventAnalysis,
        ILogger<TriggerFullSyncHandler> logger)
    {
        _glucoseFetch = glucoseFetch;
        _notesSync = notesSync;
        _eventAnalysis = eventAnalysis;
        _logger = logger;
    }

    public async Task<TriggerFullSyncResult> Handle(TriggerFullSyncCommand request, CancellationToken ct)
    {
        _logger.LogInformation("Manual full sync requested via MediatR.");
        var results = new List<SyncSourceResult>();

        // Sync glucose data
        try
        {
            var (_, glucoseMsg) = await _glucoseFetch.TriggerSyncAsync();
            results.Add(new SyncSourceResult("Glucose", true, glucoseMsg));
            _logger.LogInformation("Glucose sync result: {Message}", glucoseMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Glucose sync failed during manual trigger.");
            results.Add(new SyncSourceResult("Glucose", false, ex.Message));
        }

        // Sync Samsung Notes
        try
        {
            var notesMsg = await _notesSync.TriggerSyncAsync();
            results.Add(new SyncSourceResult("Notes", true, notesMsg));
            _logger.LogInformation("Notes sync result: {Message}", notesMsg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notes sync failed during manual trigger.");
            results.Add(new SyncSourceResult("Notes", false, ex.Message));
        }

        // Process events from synced notes (create events + run AI analysis)
        try
        {
            await _eventAnalysis.TriggerProcessingAsync(ct);
            results.Add(new SyncSourceResult("Events", true, "Event processing completed."));
            _logger.LogInformation("Event processing completed after sync.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event processing failed during manual trigger.");
            results.Add(new SyncSourceResult("Events", false, ex.Message));
        }

        var allSuccess = results.All(r => r.Success);
        return new TriggerFullSyncResult(
            allSuccess,
            allSuccess ? "All syncs completed successfully." : "Sync completed with some issues.",
            results);
    }
}

// ── TriggerGlucoseSync ────────────────────────────────────────

public record TriggerGlucoseSyncCommand : IRequest<SyncSourceResult>;

public class TriggerGlucoseSyncHandler : IRequestHandler<TriggerGlucoseSyncCommand, SyncSourceResult>
{
    private readonly GlucoseFetchService _glucoseFetch;
    private readonly ILogger<TriggerGlucoseSyncHandler> _logger;

    public TriggerGlucoseSyncHandler(GlucoseFetchService glucoseFetch, ILogger<TriggerGlucoseSyncHandler> logger)
    {
        _glucoseFetch = glucoseFetch;
        _logger = logger;
    }

    public async Task<SyncSourceResult> Handle(TriggerGlucoseSyncCommand request, CancellationToken ct)
    {
        _logger.LogInformation("Manual glucose sync requested via MediatR.");
        try
        {
            var (_, msg) = await _glucoseFetch.TriggerSyncAsync();
            return new SyncSourceResult("Glucose", true, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Glucose sync failed during manual trigger.");
            return new SyncSourceResult("Glucose", false, ex.Message);
        }
    }
}

// ── TriggerNotesSync ──────────────────────────────────────────

public record TriggerNotesSyncCommand : IRequest<SyncSourceResult>;

public class TriggerNotesSyncHandler : IRequestHandler<TriggerNotesSyncCommand, SyncSourceResult>
{
    private readonly SamsungNotesSyncService _notesSync;
    private readonly ILogger<TriggerNotesSyncHandler> _logger;

    public TriggerNotesSyncHandler(SamsungNotesSyncService notesSync, ILogger<TriggerNotesSyncHandler> logger)
    {
        _notesSync = notesSync;
        _logger = logger;
    }

    public async Task<SyncSourceResult> Handle(TriggerNotesSyncCommand request, CancellationToken ct)
    {
        _logger.LogInformation("Manual notes sync requested via MediatR.");
        try
        {
            var msg = await _notesSync.TriggerSyncAsync();
            return new SyncSourceResult("Notes", true, msg);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notes sync failed during manual trigger.");
            return new SyncSourceResult("Notes", false, ex.Message);
        }
    }
}
