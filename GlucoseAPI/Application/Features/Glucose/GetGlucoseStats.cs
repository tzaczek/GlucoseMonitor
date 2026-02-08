using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Glucose;

public record GetGlucoseStatsQuery(int Hours = 24) : IRequest<GlucoseStatsDto?>;

public class GetGlucoseStatsHandler : IRequestHandler<GetGlucoseStatsQuery, GlucoseStatsDto?>
{
    private readonly GlucoseDbContext _db;

    public GetGlucoseStatsHandler(GlucoseDbContext db) => _db = db;

    public async Task<GlucoseStatsDto?> Handle(GetGlucoseStatsQuery request, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddHours(-request.Hours);

        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= since)
            .ToListAsync(ct);

        if (readings.Count == 0)
            return null;

        var latest = readings.OrderByDescending(r => r.Timestamp).First();
        var inRangeCount = readings.Count(r => r.Value >= 70 && r.Value <= 180);

        return new GlucoseStatsDto
        {
            Average = Math.Round(readings.Average(r => r.Value), 1),
            Min = readings.Min(r => r.Value),
            Max = readings.Max(r => r.Value),
            TotalReadings = readings.Count,
            TimeInRange = Math.Round((double)inRangeCount / readings.Count * 100, 1),
            LatestReading = GetLatestReadingHandler.MapToDto(latest)
        };
    }
}
