using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Glucose;

public record GetLatestReadingQuery : IRequest<GlucoseReadingDto?>;

public class GetLatestReadingHandler : IRequestHandler<GetLatestReadingQuery, GlucoseReadingDto?>
{
    private readonly GlucoseDbContext _db;

    public GetLatestReadingHandler(GlucoseDbContext db) => _db = db;

    public async Task<GlucoseReadingDto?> Handle(GetLatestReadingQuery request, CancellationToken ct)
    {
        var reading = await _db.GlucoseReadings
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefaultAsync(ct);

        return reading == null ? null : MapToDto(reading);
    }

    internal static GlucoseReadingDto MapToDto(GlucoseReading r) => new()
    {
        Id = r.Id,
        Value = r.Value,
        Timestamp = DateTime.SpecifyKind(r.Timestamp, DateTimeKind.Utc),
        TrendArrow = r.TrendArrow,
        IsHigh = r.IsHigh,
        IsLow = r.IsLow,
        PatientId = r.PatientId
    };
}
