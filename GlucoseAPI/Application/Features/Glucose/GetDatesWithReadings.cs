using GlucoseAPI.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Glucose;

public record GetDatesWithReadingsQuery : IRequest<List<string>>;

public class GetDatesWithReadingsHandler : IRequestHandler<GetDatesWithReadingsQuery, List<string>>
{
    private readonly GlucoseDbContext _db;

    public GetDatesWithReadingsHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<string>> Handle(GetDatesWithReadingsQuery request, CancellationToken ct)
    {
        return await _db.GlucoseReadings
            .Select(r => r.Timestamp.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .Select(d => d.ToString("yyyy-MM-dd"))
            .ToListAsync(ct);
    }
}
