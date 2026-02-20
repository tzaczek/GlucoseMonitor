using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Glucose;

public record GetGlucoseRangeQuery(DateTime Start, DateTime End) : IRequest<GlucoseRangeResult>;

public record GlucoseRangeResult(List<GlucoseReadingDto> Readings, List<GlucoseRangeEventDto> Events);

public record GlucoseRangeEventDto(
    int Id, string Title, DateTime Timestamp, double? GlucoseAtEvent, double? GlucoseSpike, string? Classification);

public class GetGlucoseRangeHandler : IRequestHandler<GetGlucoseRangeQuery, GlucoseRangeResult>
{
    private readonly GlucoseDbContext _db;

    public GetGlucoseRangeHandler(GlucoseDbContext db) => _db = db;

    public async Task<GlucoseRangeResult> Handle(GetGlucoseRangeQuery request, CancellationToken ct)
    {
        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= request.Start && r.Timestamp < request.End)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var events = await _db.GlucoseEvents
            .Where(e => e.EventTimestamp >= request.Start && e.EventTimestamp < request.End)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        return new GlucoseRangeResult(
            readings.Select(GetLatestReadingHandler.MapToDto).ToList(),
            events.Select(e => new GlucoseRangeEventDto(
                e.Id,
                e.NoteTitle ?? "Event",
                DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
                e.GlucoseAtEvent,
                e.GlucoseSpike,
                e.AiClassification
            )).ToList()
        );
    }
}
