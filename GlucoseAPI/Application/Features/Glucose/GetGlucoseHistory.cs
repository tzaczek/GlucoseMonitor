using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Glucose;

public record GetGlucoseHistoryQuery(int Hours = 24, int? Limit = null)
    : IRequest<List<GlucoseReadingDto>>;

public class GetGlucoseHistoryHandler : IRequestHandler<GetGlucoseHistoryQuery, List<GlucoseReadingDto>>
{
    private readonly GlucoseDbContext _db;

    public GetGlucoseHistoryHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<GlucoseReadingDto>> Handle(GetGlucoseHistoryQuery request, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-request.Hours);

        var query = _db.GlucoseReadings
            .Where(r => r.Timestamp >= since)
            .OrderByDescending(r => r.Timestamp)
            .AsQueryable();

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var readings = await query.ToListAsync(ct);
        return readings.Select(GetLatestReadingHandler.MapToDto).ToList();
    }
}
