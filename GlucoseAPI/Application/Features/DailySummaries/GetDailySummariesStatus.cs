using GlucoseAPI.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.DailySummaries;

public record GetDailySummariesStatusQuery : IRequest<DailySummariesStatusResult>;

public record DailySummariesStatusResult(int TotalSummaries, int ProcessedSummaries, int PendingSummaries);

public class GetDailySummariesStatusHandler
    : IRequestHandler<GetDailySummariesStatusQuery, DailySummariesStatusResult>
{
    private readonly GlucoseDbContext _db;

    public GetDailySummariesStatusHandler(GlucoseDbContext db) => _db = db;

    public async Task<DailySummariesStatusResult> Handle(
        GetDailySummariesStatusQuery request, CancellationToken ct)
    {
        var total = await _db.DailySummaries.CountAsync(ct);
        var processed = await _db.DailySummaries.CountAsync(s => s.IsProcessed, ct);
        return new DailySummariesStatusResult(total, processed, total - processed);
    }
}
