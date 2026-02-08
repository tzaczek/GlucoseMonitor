using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;

namespace GlucoseAPI.Application.Features.DailySummaries;

public record GetSnapshotDetailQuery(int SnapshotId) : IRequest<DailySummarySnapshotDetailDto?>;

public class GetSnapshotDetailHandler
    : IRequestHandler<GetSnapshotDetailQuery, DailySummarySnapshotDetailDto?>
{
    private readonly GlucoseDbContext _db;

    public GetSnapshotDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<DailySummarySnapshotDetailDto?> Handle(
        GetSnapshotDetailQuery request, CancellationToken ct)
    {
        var snap = await _db.DailySummarySnapshots.FindAsync(new object[] { request.SnapshotId }, ct);
        if (snap == null) return null;

        return new DailySummarySnapshotDetailDto
        {
            Id = snap.Id,
            Date = snap.Date,
            GeneratedAt = DateTime.SpecifyKind(snap.GeneratedAt, DateTimeKind.Utc),
            Trigger = snap.Trigger,
            DataStartUtc = DateTime.SpecifyKind(snap.DataStartUtc, DateTimeKind.Utc),
            DataEndUtc = DateTime.SpecifyKind(snap.DataEndUtc, DateTimeKind.Utc),
            FirstReadingUtc = snap.FirstReadingUtc.HasValue
                ? DateTime.SpecifyKind(snap.FirstReadingUtc.Value, DateTimeKind.Utc) : null,
            LastReadingUtc = snap.LastReadingUtc.HasValue
                ? DateTime.SpecifyKind(snap.LastReadingUtc.Value, DateTimeKind.Utc) : null,
            TimeZone = snap.TimeZone,
            EventCount = snap.EventCount,
            EventIds = snap.EventIds,
            EventTitles = snap.EventTitles,
            ReadingCount = snap.ReadingCount,
            GlucoseMin = snap.GlucoseMin,
            GlucoseMax = snap.GlucoseMax,
            GlucoseAvg = snap.GlucoseAvg,
            GlucoseStdDev = snap.GlucoseStdDev,
            TimeInRange = snap.TimeInRange,
            TimeAboveRange = snap.TimeAboveRange,
            TimeBelowRange = snap.TimeBelowRange,
            AiAnalysis = snap.AiAnalysis,
            AiClassification = snap.AiClassification,
            IsProcessed = snap.IsProcessed,
            HasAnalysis = !string.IsNullOrEmpty(snap.AiAnalysis)
        };
    }
}
