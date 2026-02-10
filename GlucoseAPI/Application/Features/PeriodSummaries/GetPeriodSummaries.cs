using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.PeriodSummaries;

public record GetPeriodSummariesQuery(int? Limit = null) : IRequest<List<PeriodSummaryListDto>>;

public class GetPeriodSummariesHandler : IRequestHandler<GetPeriodSummariesQuery, List<PeriodSummaryListDto>>
{
    private readonly GlucoseDbContext _db;
    public GetPeriodSummariesHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<PeriodSummaryListDto>> Handle(GetPeriodSummariesQuery request, CancellationToken ct)
    {
        var query = _db.PeriodSummaries
            .OrderByDescending(s => s.CreatedAt)
            .AsQueryable();

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var summaries = await query.ToListAsync(ct);

        return summaries.Select(s => new PeriodSummaryListDto
        {
            Id = s.Id,
            Name = s.Name,
            PeriodStart = DateTime.SpecifyKind(s.PeriodStart, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(s.PeriodEnd, DateTimeKind.Utc),
            Status = s.Status,
            AiClassification = s.AiClassification,
            ReadingCount = s.ReadingCount,
            GlucoseAvg = s.GlucoseAvg,
            TimeInRange = s.TimeInRange,
            EventCount = s.EventCount,
            CreatedAt = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc)
        }).ToList();
    }
}
