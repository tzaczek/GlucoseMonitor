using GlucoseAPI.Data;
using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.Events;

public record ReprocessEventCommand(int Id, string? ModelOverride = null) : IRequest<ReprocessEventResult>;

public record ReprocessEventResult(bool Found, bool Success, string Message, string? Analysis = null);

public class ReprocessEventHandler : IRequestHandler<ReprocessEventCommand, ReprocessEventResult>
{
    private readonly GlucoseDbContext _db;
    private readonly EventAnalyzer _analyzer;
    private readonly ILogger<ReprocessEventHandler> _logger;

    public ReprocessEventHandler(GlucoseDbContext db, EventAnalyzer analyzer, ILogger<ReprocessEventHandler> logger)
    {
        _db = db;
        _analyzer = analyzer;
        _logger = logger;
    }

    public async Task<ReprocessEventResult> Handle(ReprocessEventCommand request, CancellationToken ct)
    {
        var evt = await _db.GlucoseEvents.FindAsync(new object[] { request.Id }, ct);
        if (evt == null)
            return new ReprocessEventResult(false, false, "Event not found.");

        _logger.LogInformation("Immediate reprocess requested for event {Id} '{Title}'.", request.Id, evt.NoteTitle);

        try
        {
            var analysis = await _analyzer.AnalyzeEventAsync(evt, "Manual re-analysis requested by user", ct, request.ModelOverride);

            return analysis != null
                ? new ReprocessEventResult(true, true, "Analysis completed successfully.", analysis)
                : new ReprocessEventResult(true, false, "Analysis could not be completed. Check GPT API key in Settings.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed immediate reprocess for event {Id}.", request.Id);
            return new ReprocessEventResult(true, false, $"Analysis failed: {ex.Message}");
        }
    }
}
