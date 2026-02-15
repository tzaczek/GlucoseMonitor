using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Comparisons;

public record GetComparisonDetailQuery(int Id) : IRequest<ComparisonDetailDto?>;

public class GetComparisonDetailHandler : IRequestHandler<GetComparisonDetailQuery, ComparisonDetailDto?>
{
    private readonly GlucoseDbContext _db;
    public GetComparisonDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<ComparisonDetailDto?> Handle(GetComparisonDetailQuery request, CancellationToken ct)
    {
        var comp = await _db.GlucoseComparisons.FindAsync(new object[] { request.Id }, ct);
        if (comp == null) return null;

        // Gather readings for both periods
        var readingsA = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= comp.PeriodAStart && r.Timestamp < comp.PeriodAEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var readingsB = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= comp.PeriodBStart && r.Timestamp < comp.PeriodBEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        // Gather events for both periods
        var eventsA = await _db.GlucoseEvents
            .Where(e => e.EventTimestamp >= comp.PeriodAStart && e.EventTimestamp < comp.PeriodAEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        var eventsB = await _db.GlucoseEvents
            .Where(e => e.EventTimestamp >= comp.PeriodBStart && e.EventTimestamp < comp.PeriodBEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        return new ComparisonDetailDto
        {
            Id = comp.Id,
            Name = comp.Name,
            Status = comp.Status,
            ErrorMessage = comp.ErrorMessage,
            PeriodAStart = DateTime.SpecifyKind(comp.PeriodAStart, DateTimeKind.Utc),
            PeriodAEnd = DateTime.SpecifyKind(comp.PeriodAEnd, DateTimeKind.Utc),
            PeriodALabel = comp.PeriodALabel,
            PeriodAStats = new ComparisonPeriodStatsDto
            {
                ReadingCount = comp.PeriodAReadingCount,
                GlucoseMin = comp.PeriodAGlucoseMin,
                GlucoseMax = comp.PeriodAGlucoseMax,
                GlucoseAvg = comp.PeriodAGlucoseAvg,
                GlucoseStdDev = comp.PeriodAGlucoseStdDev,
                TimeInRange = comp.PeriodATimeInRange,
                TimeAboveRange = comp.PeriodATimeAboveRange,
                TimeBelowRange = comp.PeriodATimeBelowRange,
                EventCount = comp.PeriodAEventCount,
                EventTitles = comp.PeriodAEventTitles
            },
            PeriodAReadings = SampleReadings(readingsA, comp.PeriodAStart),
            PeriodAEvents = eventsA.Select(e => new ComparisonEventDto
            {
                Id = e.Id,
                NoteTitle = e.NoteTitle,
                NoteContent = e.NoteContent,
                EventTimestamp = DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
                GlucoseAtEvent = e.GlucoseAtEvent,
                AiClassification = e.AiClassification,
                OffsetHours = (e.EventTimestamp - comp.PeriodAStart).TotalHours
            }).ToList(),

            PeriodBStart = DateTime.SpecifyKind(comp.PeriodBStart, DateTimeKind.Utc),
            PeriodBEnd = DateTime.SpecifyKind(comp.PeriodBEnd, DateTimeKind.Utc),
            PeriodBLabel = comp.PeriodBLabel,
            PeriodBStats = new ComparisonPeriodStatsDto
            {
                ReadingCount = comp.PeriodBReadingCount,
                GlucoseMin = comp.PeriodBGlucoseMin,
                GlucoseMax = comp.PeriodBGlucoseMax,
                GlucoseAvg = comp.PeriodBGlucoseAvg,
                GlucoseStdDev = comp.PeriodBGlucoseStdDev,
                TimeInRange = comp.PeriodBTimeInRange,
                TimeAboveRange = comp.PeriodBTimeAboveRange,
                TimeBelowRange = comp.PeriodBTimeBelowRange,
                EventCount = comp.PeriodBEventCount,
                EventTitles = comp.PeriodBEventTitles
            },
            PeriodBReadings = SampleReadings(readingsB, comp.PeriodBStart),
            PeriodBEvents = eventsB.Select(e => new ComparisonEventDto
            {
                Id = e.Id,
                NoteTitle = e.NoteTitle,
                NoteContent = e.NoteContent,
                EventTimestamp = DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
                GlucoseAtEvent = e.GlucoseAtEvent,
                AiClassification = e.AiClassification,
                OffsetHours = (e.EventTimestamp - comp.PeriodBStart).TotalHours
            }).ToList(),

            AiAnalysis = comp.AiAnalysis,
            AiClassification = comp.AiClassification,
            AiModel = comp.AiModel,
            CreatedAt = DateTime.SpecifyKind(comp.CreatedAt, DateTimeKind.Utc),
            CompletedAt = comp.CompletedAt.HasValue
                ? DateTime.SpecifyKind(comp.CompletedAt.Value, DateTimeKind.Utc)
                : null
        };
    }

    /// <summary>
    /// Sample readings to keep payload reasonable (max ~500 per period for the chart).
    /// </summary>
    private static List<ComparisonReadingDto> SampleReadings(List<GlucoseReading> readings, DateTime periodStart)
    {
        var sampled = readings.Count > 500
            ? readings.Where((_, i) => i % (readings.Count / 500 + 1) == 0).ToList()
            : readings;

        return sampled.Select(r => new ComparisonReadingDto
        {
            Timestamp = DateTime.SpecifyKind(r.Timestamp, DateTimeKind.Utc),
            Value = r.Value,
            OffsetHours = Math.Round((r.Timestamp - periodStart).TotalHours, 3)
        }).ToList();
    }
}
