using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Comparisons;

public record GetComparisonsQuery(int? Limit = null) : IRequest<List<ComparisonSummaryDto>>;

public class GetComparisonsHandler : IRequestHandler<GetComparisonsQuery, List<ComparisonSummaryDto>>
{
    private readonly GlucoseDbContext _db;
    public GetComparisonsHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<ComparisonSummaryDto>> Handle(GetComparisonsQuery request, CancellationToken ct)
    {
        var query = _db.GlucoseComparisons
            .OrderByDescending(c => c.CreatedAt)
            .AsQueryable();

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var comparisons = await query.ToListAsync(ct);

        return comparisons.Select(c => new ComparisonSummaryDto
        {
            Id = c.Id,
            Name = c.Name,
            PeriodAStart = DateTime.SpecifyKind(c.PeriodAStart, DateTimeKind.Utc),
            PeriodAEnd = DateTime.SpecifyKind(c.PeriodAEnd, DateTimeKind.Utc),
            PeriodALabel = c.PeriodALabel,
            PeriodBStart = DateTime.SpecifyKind(c.PeriodBStart, DateTimeKind.Utc),
            PeriodBEnd = DateTime.SpecifyKind(c.PeriodBEnd, DateTimeKind.Utc),
            PeriodBLabel = c.PeriodBLabel,
            Status = c.Status,
            AiClassification = c.AiClassification,
            PeriodAGlucoseAvg = c.PeriodAGlucoseAvg,
            PeriodBGlucoseAvg = c.PeriodBGlucoseAvg,
            PeriodATimeInRange = c.PeriodATimeInRange,
            PeriodBTimeInRange = c.PeriodBTimeInRange,
            PeriodAEventCount = c.PeriodAEventCount,
            PeriodBEventCount = c.PeriodBEventCount,
            CreatedAt = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc)
        }).ToList();
    }
}
