using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.EventLogs;

public record GetEventLogsQuery(
    int? Limit = 200,
    int? Offset = null,
    string? Level = null,
    string? Category = null,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null
) : IRequest<GetEventLogsResult>;

public record GetEventLogsResult(List<EventLogDto> Logs, int TotalCount);

public class GetEventLogsHandler : IRequestHandler<GetEventLogsQuery, GetEventLogsResult>
{
    private readonly GlucoseDbContext _db;
    public GetEventLogsHandler(GlucoseDbContext db) => _db = db;

    public async Task<GetEventLogsResult> Handle(GetEventLogsQuery request, CancellationToken ct)
    {
        var query = _db.EventLogs.AsQueryable();

        // ── Filters ──
        if (!string.IsNullOrWhiteSpace(request.Level))
            query = query.Where(e => e.Level == request.Level);

        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(e => e.Category == request.Category);

        if (request.From.HasValue)
            query = query.Where(e => e.Timestamp >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(e => e.Timestamp <= request.To.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(e => e.Message.Contains(request.Search) ||
                                     (e.Detail != null && e.Detail.Contains(request.Search)) ||
                                     (e.Source != null && e.Source.Contains(request.Search)));

        var totalCount = await query.CountAsync(ct);

        // ── Sort + paginate ──
        var ordered = query.OrderByDescending(e => e.Timestamp);

        if (request.Offset.HasValue && request.Offset.Value > 0)
            ordered = (IOrderedQueryable<EventLog>)ordered.Skip(request.Offset.Value);

        var limit = Math.Clamp(request.Limit ?? 200, 1, 1000);

        var logs = await ordered
            .Take(limit)
            .Select(e => new EventLogDto
            {
                Id = e.Id,
                Timestamp = DateTime.SpecifyKind(e.Timestamp, DateTimeKind.Utc),
                Level = e.Level,
                Category = e.Category,
                Message = e.Message,
                Detail = e.Detail,
                Source = e.Source,
                RelatedEntityId = e.RelatedEntityId,
                RelatedEntityType = e.RelatedEntityType,
                NumericValue = e.NumericValue,
                DurationMs = e.DurationMs,
            })
            .ToListAsync(ct);

        return new GetEventLogsResult(logs, totalCount);
    }
}
