using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Events;

public record GetEventsQuery(int? Limit = null) : IRequest<List<GlucoseEventSummaryDto>>;

public class GetEventsHandler : IRequestHandler<GetEventsQuery, List<GlucoseEventSummaryDto>>
{
    private readonly GlucoseDbContext _db;

    public GetEventsHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<GlucoseEventSummaryDto>> Handle(GetEventsQuery request, CancellationToken ct)
    {
        var query = _db.GlucoseEvents
            .OrderByDescending(e => e.EventTimestamp)
            .AsQueryable();

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var events = await query.ToListAsync(ct);

        var eventIds = events.Select(e => e.Id).ToList();
        var analysisCounts = await _db.EventAnalysisHistory
            .Where(h => eventIds.Contains(h.GlucoseEventId))
            .GroupBy(h => h.GlucoseEventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EventId, x => x.Count, ct);

        return events.Select(e => MapToSummaryDto(e, analysisCounts)).ToList();
    }

    internal static GlucoseEventSummaryDto MapToSummaryDto(
        GlucoseEvent e, Dictionary<int, int>? analysisCounts = null) => new()
    {
        Id = e.Id,
        NoteTitle = e.NoteTitle,
        NoteContentPreview = e.NoteContent != null && e.NoteContent.Length > 120
            ? e.NoteContent[..120] + "…"
            : e.NoteContent,
        EventTimestamp = DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
        ReadingCount = e.ReadingCount,
        GlucoseAtEvent = e.GlucoseAtEvent,
        GlucoseMin = e.GlucoseMin,
        GlucoseMax = e.GlucoseMax,
        GlucoseAvg = e.GlucoseAvg,
        GlucoseSpike = e.GlucoseSpike,
        IsProcessed = e.IsProcessed,
        HasAnalysis = !string.IsNullOrEmpty(e.AiAnalysis),
        AiClassification = e.AiClassification,
        AnalysisCount = analysisCounts?.GetValueOrDefault(e.Id, 0) ?? 0
    };
}
