using GlucoseAPI.Data;
using GlucoseAPI.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Food;

// ── Trigger full scan ────────────────────────────────────────

public record TriggerFoodScanCommand() : IRequest<string>;

public class TriggerFoodScanHandler : IRequestHandler<TriggerFoodScanCommand, string>
{
    private readonly FoodPatternService _service;

    public TriggerFoodScanHandler(FoodPatternService service) => _service = service;

    public Task<string> Handle(TriggerFoodScanCommand request, CancellationToken ct)
    {
        _service.RequestFullScan();
        return Task.FromResult("Full food pattern scan queued.");
    }
}

// ── Delete a food item ───────────────────────────────────────

public record DeleteFoodItemCommand(int Id) : IRequest<bool>;

public class DeleteFoodItemHandler : IRequestHandler<DeleteFoodItemCommand, bool>
{
    private readonly GlucoseDbContext _db;

    public DeleteFoodItemHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteFoodItemCommand request, CancellationToken ct)
    {
        var food = await _db.FoodItems.FindAsync(new object[] { request.Id }, ct);
        if (food == null) return false;

        _db.FoodItems.Remove(food);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Merge food items ─────────────────────────────────────────

public record MergeFoodItemsCommand(int TargetId, List<int> SourceIds) : IRequest<bool>;

public class MergeFoodItemsHandler : IRequestHandler<MergeFoodItemsCommand, bool>
{
    private readonly GlucoseDbContext _db;

    public MergeFoodItemsHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(MergeFoodItemsCommand request, CancellationToken ct)
    {
        var target = await _db.FoodItems.FindAsync(new object[] { request.TargetId }, ct);
        if (target == null) return false;

        foreach (var sourceId in request.SourceIds)
        {
            if (sourceId == request.TargetId) continue;

            var sourceLinks = await _db.FoodEventLinks
                .Where(l => l.FoodItemId == sourceId)
                .ToListAsync(ct);

            foreach (var link in sourceLinks)
            {
                var exists = await _db.FoodEventLinks
                    .AnyAsync(l => l.FoodItemId == request.TargetId && l.GlucoseEventId == link.GlucoseEventId, ct);

                if (!exists)
                {
                    link.FoodItemId = request.TargetId;
                }
                else
                {
                    _db.FoodEventLinks.Remove(link);
                }
            }

            var source = await _db.FoodItems.FindAsync(new object[] { sourceId }, ct);
            if (source != null) _db.FoodItems.Remove(source);
        }

        await _db.SaveChangesAsync(ct);

        // Recalculate target aggregates
        var links = await _db.FoodEventLinks
            .Where(l => l.FoodItemId == target.Id)
            .Include(l => l.GlucoseEvent)
            .ToListAsync(ct);

        target.OccurrenceCount = links.Count;
        var spikes = links.Where(l => l.Spike.HasValue).Select(l => l.Spike!.Value).ToList();
        target.AvgSpike = spikes.Count > 0 ? spikes.Average() : null;
        target.WorstSpike = spikes.Count > 0 ? spikes.Max() : null;
        target.BestSpike = spikes.Count > 0 ? spikes.Min() : null;

        var classifications = links.Select(l => l.AiClassification).ToList();
        target.GreenCount = classifications.Count(c => c == "green");
        target.YellowCount = classifications.Count(c => c == "yellow");
        target.RedCount = classifications.Count(c => c == "red");

        var timestamps = links.Where(l => l.GlucoseEvent != null).Select(l => l.GlucoseEvent!.EventTimestamp).ToList();
        if (timestamps.Count > 0)
        {
            target.FirstSeen = timestamps.Min();
            target.LastSeen = timestamps.Max();
        }
        target.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── Rename a food item ───────────────────────────────────────

public record RenameFoodItemCommand(int Id, string NewName) : IRequest<bool>;

public class RenameFoodItemHandler : IRequestHandler<RenameFoodItemCommand, bool>
{
    private readonly GlucoseDbContext _db;

    public RenameFoodItemHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(RenameFoodItemCommand request, CancellationToken ct)
    {
        var food = await _db.FoodItems.FindAsync(new object[] { request.Id }, ct);
        if (food == null) return false;

        food.Name = request.NewName.Trim();
        food.NormalizedName = request.NewName.Trim().ToLowerInvariant();
        food.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}
