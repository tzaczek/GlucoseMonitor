using GlucoseAPI.Application.Common;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.DailySummaries;

public record GetDailySummariesQuery(int? Limit = null, int Offset = 0) : IRequest<PagedResult<DailySummaryListDto>>;

public class GetDailySummariesHandler : IRequestHandler<GetDailySummariesQuery, PagedResult<DailySummaryListDto>>
{
    private readonly GlucoseDbContext _db;

    public GetDailySummariesHandler(GlucoseDbContext db) => _db = db;

    public async Task<PagedResult<DailySummaryListDto>> Handle(GetDailySummariesQuery request, CancellationToken ct)
    {
        var baseQuery = _db.DailySummaries
            .OrderByDescending(s => s.Date)
            .AsQueryable();

        var totalCount = await baseQuery.CountAsync(ct);

        var query = baseQuery.Skip(request.Offset);

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var summaries = await query.ToListAsync(ct);

        var summaryIds = summaries.Select(s => s.Id).ToList();
        var snapshotCounts = await _db.DailySummarySnapshots
            .Where(snap => summaryIds.Contains(snap.DailySummaryId))
            .GroupBy(snap => snap.DailySummaryId)
            .Select(g => new { SummaryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SummaryId, x => x.Count, ct);

        var items = summaries.Select(s => new DailySummaryListDto
        {
            Id = s.Id,
            Date = s.Date,
            EventCount = s.EventCount,
            EventTitles = s.EventTitles,
            ReadingCount = s.ReadingCount,
            GlucoseMin = s.GlucoseMin,
            GlucoseMax = s.GlucoseMax,
            GlucoseAvg = s.GlucoseAvg,
            TimeInRange = s.TimeInRange,
            IsProcessed = s.IsProcessed,
            HasAnalysis = !string.IsNullOrEmpty(s.AiAnalysis),
            AiClassification = s.AiClassification,
            SnapshotCount = snapshotCounts.GetValueOrDefault(s.Id, 0)
        }).ToList();
        return new PagedResult<DailySummaryListDto>(items, totalCount);
    }
}
