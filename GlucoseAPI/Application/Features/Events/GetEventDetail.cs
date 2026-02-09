using GlucoseAPI.Application.Features.Glucose;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Events;

public record GetEventDetailQuery(int Id) : IRequest<GlucoseEventDetailDto?>;

public class GetEventDetailHandler : IRequestHandler<GetEventDetailQuery, GlucoseEventDetailDto?>
{
    private readonly GlucoseDbContext _db;

    public GetEventDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<GlucoseEventDetailDto?> Handle(GetEventDetailQuery request, CancellationToken ct)
    {
        var evt = await _db.GlucoseEvents.FindAsync(new object[] { request.Id }, ct);
        if (evt == null) return null;

        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= evt.PeriodStart && r.Timestamp <= evt.PeriodEnd)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        // Recalculate stats live using domain service
        var stats = GlucoseStatsCalculator.ComputeEventStats(readings, evt.EventTimestamp);

        var analysisHistory = await _db.EventAnalysisHistory
            .Where(h => h.GlucoseEventId == evt.Id)
            .OrderByDescending(h => h.AnalyzedAt)
            .ToListAsync(ct);

        // Find other events whose timestamps fall within this event's glucose window
        var overlappingEvents = await _db.GlucoseEvents
            .Where(e => e.Id != evt.Id
                && e.EventTimestamp >= evt.PeriodStart
                && e.EventTimestamp <= evt.PeriodEnd)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        return new GlucoseEventDetailDto
        {
            Id = evt.Id,
            NoteTitle = evt.NoteTitle,
            NoteContent = evt.NoteContent,
            EventTimestamp = DateTime.SpecifyKind(evt.EventTimestamp, DateTimeKind.Utc),
            PeriodStart = DateTime.SpecifyKind(evt.PeriodStart, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(evt.PeriodEnd, DateTimeKind.Utc),
            ReadingCount = stats.ReadingCount > 0 ? stats.ReadingCount : evt.ReadingCount,
            GlucoseAtEvent = stats.GlucoseAtEvent ?? evt.GlucoseAtEvent,
            GlucoseMin = stats.Min ?? evt.GlucoseMin,
            GlucoseMax = stats.Max ?? evt.GlucoseMax,
            GlucoseAvg = stats.Avg ?? evt.GlucoseAvg,
            GlucoseSpike = stats.Spike ?? evt.GlucoseSpike,
            PeakTime = stats.PeakTime ?? evt.PeakTime,
            AiAnalysis = evt.AiAnalysis,
            AiClassification = evt.AiClassification,
            IsProcessed = evt.IsProcessed,
            ProcessedAt = evt.ProcessedAt.HasValue
                ? DateTime.SpecifyKind(evt.ProcessedAt.Value, DateTimeKind.Utc)
                : null,
            Readings = readings.Select(GetLatestReadingHandler.MapToDto).ToList(),
            AnalysisHistory = analysisHistory.Select(MapHistoryDto).ToList(),
            OverlappingEvents = overlappingEvents.Select(e => new OverlappingEventDto
            {
                Id = e.Id,
                NoteTitle = e.NoteTitle,
                NoteContent = e.NoteContent,
                EventTimestamp = DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
                GlucoseAtEvent = e.GlucoseAtEvent,
                AiClassification = e.AiClassification
            }).ToList()
        };
    }

    internal static EventAnalysisHistoryDto MapHistoryDto(EventAnalysisHistory h) => new()
    {
        Id = h.Id,
        AiAnalysis = h.AiAnalysis,
        AiClassification = h.AiClassification,
        AnalyzedAt = DateTime.SpecifyKind(h.AnalyzedAt, DateTimeKind.Utc),
        PeriodStart = DateTime.SpecifyKind(h.PeriodStart, DateTimeKind.Utc),
        PeriodEnd = DateTime.SpecifyKind(h.PeriodEnd, DateTimeKind.Utc),
        ReadingCount = h.ReadingCount,
        Reason = h.Reason,
        GlucoseAtEvent = h.GlucoseAtEvent,
        GlucoseMin = h.GlucoseMin,
        GlucoseMax = h.GlucoseMax,
        GlucoseAvg = h.GlucoseAvg,
        GlucoseSpike = h.GlucoseSpike,
        PeakTime = h.PeakTime.HasValue
            ? DateTime.SpecifyKind(h.PeakTime.Value, DateTimeKind.Utc)
            : null
    };
}
