using GlucoseAPI.Data;
using MediatR;

namespace GlucoseAPI.Application.Features.PeriodSummaries;

public record DeletePeriodSummaryCommand(int Id) : IRequest<bool>;

public class DeletePeriodSummaryHandler : IRequestHandler<DeletePeriodSummaryCommand, bool>
{
    private readonly GlucoseDbContext _db;
    public DeletePeriodSummaryHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(DeletePeriodSummaryCommand request, CancellationToken ct)
    {
        var summary = await _db.PeriodSummaries.FindAsync(new object[] { request.Id }, ct);
        if (summary == null) return false;

        _db.PeriodSummaries.Remove(summary);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
