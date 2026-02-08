using GlucoseAPI.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Events;

public record GetEventsStatusQuery : IRequest<EventsStatusResult>;

public record EventsStatusResult(int TotalEvents, int ProcessedEvents, int PendingEvents);

public class GetEventsStatusHandler : IRequestHandler<GetEventsStatusQuery, EventsStatusResult>
{
    private readonly GlucoseDbContext _db;

    public GetEventsStatusHandler(GlucoseDbContext db) => _db = db;

    public async Task<EventsStatusResult> Handle(GetEventsStatusQuery request, CancellationToken ct)
    {
        var total = await _db.GlucoseEvents.CountAsync(ct);
        var processed = await _db.GlucoseEvents.CountAsync(e => e.IsProcessed, ct);
        return new EventsStatusResult(total, processed, total - processed);
    }
}
