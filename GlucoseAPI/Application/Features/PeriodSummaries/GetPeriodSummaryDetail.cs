using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.PeriodSummaries;

public record GetPeriodSummaryDetailQuery(int Id) : IRequest<PeriodSummaryDetailDto?>;

public class GetPeriodSummaryDetailHandler : IRequestHandler<GetPeriodSummaryDetailQuery, PeriodSummaryDetailDto?>
{
    private readonly GlucoseDbContext _db;
    public GetPeriodSummaryDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<PeriodSummaryDetailDto?> Handle(GetPeriodSummaryDetailQuery request, CancellationToken ct)
    {
        var summary = await _db.PeriodSummaries.FindAsync(new object[] { request.Id }, ct);
        if (summary == null) return null;

        // Gather readings for chart
        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= summary.PeriodStart && r.Timestamp < summary.PeriodEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        // Gather events
        var events = await _db.GlucoseEvents
            .Where(e => e.EventTimestamp >= summary.PeriodStart && e.EventTimestamp < summary.PeriodEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        // Sample readings to keep payload reasonable (max ~600 for chart)
        var sampledReadings = readings.Count > 600
            ? readings.Where((_, i) => i % (readings.Count / 600 + 1) == 0).ToList()
            : readings;

        return new PeriodSummaryDetailDto
        {
            Id = summary.Id,
            Name = summary.Name,
            PeriodStart = DateTime.SpecifyKind(summary.PeriodStart, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(summary.PeriodEnd, DateTimeKind.Utc),
            Status = summary.Status,
            ErrorMessage = summary.ErrorMessage,

            ReadingCount = summary.ReadingCount,
            GlucoseMin = summary.GlucoseMin,
            GlucoseMax = summary.GlucoseMax,
            GlucoseAvg = summary.GlucoseAvg,
            GlucoseStdDev = summary.GlucoseStdDev,
            TimeInRange = summary.TimeInRange,
            TimeAboveRange = summary.TimeAboveRange,
            TimeBelowRange = summary.TimeBelowRange,

            EventCount = events.Count,
            Events = events.Select(e => new PeriodSummaryEventDto
            {
                Id = e.Id,
                NoteTitle = e.NoteTitle,
                NoteContent = e.NoteContent,
                EventTimestamp = DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
                GlucoseAtEvent = e.GlucoseAtEvent,
                GlucoseSpike = e.GlucoseSpike,
                AiClassification = e.AiClassification
            }).ToList(),

            Readings = sampledReadings.Select(r => new PeriodSummaryReadingDto
            {
                Timestamp = DateTime.SpecifyKind(r.Timestamp, DateTimeKind.Utc),
                Value = r.Value
            }).ToList(),

            AiAnalysis = summary.AiAnalysis,
            AiClassification = summary.AiClassification,
            AiModel = summary.AiModel,
            CreatedAt = DateTime.SpecifyKind(summary.CreatedAt, DateTimeKind.Utc),
            CompletedAt = summary.CompletedAt.HasValue
                ? DateTime.SpecifyKind(summary.CompletedAt.Value, DateTimeKind.Utc)
                : null
        };
    }
}
