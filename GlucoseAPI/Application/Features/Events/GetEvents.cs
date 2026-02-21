using GlucoseAPI.Application.Common;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Events;

public record GetEventsQuery(int? Limit = null, int Offset = 0) : IRequest<PagedResult<GlucoseEventSummaryDto>>;

public class GetEventsHandler : IRequestHandler<GetEventsQuery, PagedResult<GlucoseEventSummaryDto>>
{
    private readonly GlucoseDbContext _db;

    public GetEventsHandler(GlucoseDbContext db) => _db = db;

    public async Task<PagedResult<GlucoseEventSummaryDto>> Handle(GetEventsQuery request, CancellationToken ct)
    {
        var baseQuery = _db.GlucoseEvents
            .OrderByDescending(e => e.EventTimestamp)
            .AsQueryable();

        var totalCount = await baseQuery.CountAsync(ct);

        var query = baseQuery.Skip(request.Offset);

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var events = await query.ToListAsync(ct);

        var eventIds = events.Select(e => e.Id).ToList();
        var analysisCounts = await _db.EventAnalysisHistory
            .Where(h => eventIds.Contains(h.GlucoseEventId))
            .GroupBy(h => h.GlucoseEventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EventId, x => x.Count, ct);

        var items = events.Select(e => MapToSummaryDto(e, analysisCounts)).ToList();
        return new PagedResult<GlucoseEventSummaryDto>(items, totalCount);
    }

    internal static GlucoseEventSummaryDto MapToSummaryDto(
        GlucoseEvent e, Dictionary<int, int>? analysisCounts = null) => new()
    {
        Id = e.Id,
        NoteTitle = e.NoteTitle,
        NoteTitleEn = e.NoteTitleEn,
        NoteContentPreview = e.NoteContent != null && e.NoteContent.Length > 120
            ? e.NoteContent[..120] + "…"
            : e.NoteContent,
        NoteContentPreviewEn = e.NoteContentEn != null && e.NoteContentEn.Length > 120
            ? e.NoteContentEn[..120] + "…"
            : e.NoteContentEn,
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
