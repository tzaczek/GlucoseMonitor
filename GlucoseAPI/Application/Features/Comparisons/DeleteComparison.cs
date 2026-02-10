using GlucoseAPI.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Comparisons;

public record DeleteComparisonCommand(int Id) : IRequest<bool>;

public class DeleteComparisonHandler : IRequestHandler<DeleteComparisonCommand, bool>
{
    private readonly GlucoseDbContext _db;
    public DeleteComparisonHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteComparisonCommand request, CancellationToken ct)
    {
        var comp = await _db.GlucoseComparisons.FindAsync(new object[] { request.Id }, ct);
        if (comp == null) return false;

        _db.GlucoseComparisons.Remove(comp);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
