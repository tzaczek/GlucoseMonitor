using GlucoseAPI.Application.Common;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Food;

// ── List all food items ──────────────────────────────────────

public record GetFoodItemsQuery(string? Search, string? SortBy, bool Descending = true, int? Limit = null, int Offset = 0) : IRequest<PagedResult<FoodItemSummaryDto>>;

public class GetFoodItemsHandler : IRequestHandler<GetFoodItemsQuery, PagedResult<FoodItemSummaryDto>>
{
    private readonly GlucoseDbContext _db;

    public GetFoodItemsHandler(GlucoseDbContext db) => _db = db;

    public async Task<PagedResult<FoodItemSummaryDto>> Handle(GetFoodItemsQuery request, CancellationToken ct)
    {
        var query = _db.FoodItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.ToLower();
            query = query.Where(f => f.NormalizedName.Contains(search)
                || (f.NameEn != null && f.NameEn.ToLower().Contains(search)));
        }

        query = request.SortBy?.ToLower() switch
        {
            "name" => request.Descending ? query.OrderByDescending(f => f.Name) : query.OrderBy(f => f.Name),
            "spike" => request.Descending ? query.OrderByDescending(f => f.AvgSpike) : query.OrderBy(f => f.AvgSpike),
            "count" => request.Descending ? query.OrderByDescending(f => f.OccurrenceCount) : query.OrderBy(f => f.OccurrenceCount),
            "lastseen" => request.Descending ? query.OrderByDescending(f => f.LastSeen) : query.OrderBy(f => f.LastSeen),
            _ => query.OrderByDescending(f => f.OccurrenceCount)
        };

        var totalCount = await query.CountAsync(ct);

        var pagedQuery = query.Skip(request.Offset);
        if (request.Limit.HasValue)
            pagedQuery = pagedQuery.Take(request.Limit.Value);

        var items = await pagedQuery.Select(f => new FoodItemSummaryDto
        {
            Id = f.Id,
            Name = f.Name,
            NameEn = f.NameEn,
            Category = f.Category,
            OccurrenceCount = f.OccurrenceCount,
            AvgSpike = f.AvgSpike,
            WorstSpike = f.WorstSpike,
            BestSpike = f.BestSpike,
            AvgRecoveryMinutes = f.AvgRecoveryMinutes,
            GreenCount = f.GreenCount,
            YellowCount = f.YellowCount,
            RedCount = f.RedCount,
            LastSeen = f.LastSeen
        }).ToListAsync(ct);

        return new PagedResult<FoodItemSummaryDto>(items, totalCount);
    }
}

// ── Get food item detail ─────────────────────────────────────

public record GetFoodItemDetailQuery(int Id) : IRequest<FoodItemDetailDto?>;

public class GetFoodItemDetailHandler : IRequestHandler<GetFoodItemDetailQuery, FoodItemDetailDto?>
{
    private readonly GlucoseDbContext _db;

    public GetFoodItemDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<FoodItemDetailDto?> Handle(GetFoodItemDetailQuery request, CancellationToken ct)
    {
        var food = await _db.FoodItems.FindAsync(new object[] { request.Id }, ct);
        if (food == null) return null;

        var events = await _db.FoodEventLinks
            .Where(l => l.FoodItemId == food.Id)
            .Include(l => l.GlucoseEvent)
            .OrderByDescending(l => l.GlucoseEvent!.EventTimestamp)
            .Select(l => new FoodEventDto
            {
                EventId = l.GlucoseEventId,
                NoteTitle = l.GlucoseEvent!.NoteTitle,
                NoteTitleEn = l.GlucoseEvent!.NoteTitleEn,
                NoteContent = l.GlucoseEvent!.NoteContent,
                NoteContentEn = l.GlucoseEvent!.NoteContentEn,
                EventTimestamp = l.GlucoseEvent.EventTimestamp,
                PeriodStart = l.GlucoseEvent.PeriodStart,
                PeriodEnd = l.GlucoseEvent.PeriodEnd,
                Spike = l.Spike,
                GlucoseAtEvent = l.GlucoseAtEvent,
                GlucoseMin = l.GlucoseEvent.GlucoseMin,
                GlucoseMax = l.GlucoseEvent.GlucoseMax,
                GlucoseAvg = l.GlucoseEvent.GlucoseAvg,
                AiClassification = l.AiClassification,
                AiAnalysis = l.GlucoseEvent.AiAnalysis,
                RecoveryMinutes = l.RecoveryMinutes,
                ReadingCount = l.GlucoseEvent.ReadingCount
            }).ToListAsync(ct);

        return new FoodItemDetailDto
        {
            Id = food.Id,
            Name = food.Name,
            NameEn = food.NameEn,
            Category = food.Category,
            OccurrenceCount = food.OccurrenceCount,
            AvgSpike = food.AvgSpike,
            AvgGlucoseAtEvent = food.AvgGlucoseAtEvent,
            AvgGlucoseMax = food.AvgGlucoseMax,
            AvgGlucoseMin = food.AvgGlucoseMin,
            AvgRecoveryMinutes = food.AvgRecoveryMinutes,
            WorstSpike = food.WorstSpike,
            BestSpike = food.BestSpike,
            GreenCount = food.GreenCount,
            YellowCount = food.YellowCount,
            RedCount = food.RedCount,
            FirstSeen = food.FirstSeen,
            LastSeen = food.LastSeen,
            Events = events
        };
    }
}

// ── Get food stats summary ───────────────────────────────────

public record GetFoodStatsQuery() : IRequest<FoodStatsDto>;

public record FoodStatsDto(
    int TotalFoods,
    int TotalLinks,
    int FoodsWithMultipleOccurrences,
    string? MostProblematicFood,
    double? HighestAvgSpike,
    string? SafestFood,
    double? LowestAvgSpike);

public class GetFoodStatsHandler : IRequestHandler<GetFoodStatsQuery, FoodStatsDto>
{
    private readonly GlucoseDbContext _db;

    public GetFoodStatsHandler(GlucoseDbContext db) => _db = db;

    public async Task<FoodStatsDto> Handle(GetFoodStatsQuery request, CancellationToken ct)
    {
        var foods = await _db.FoodItems.ToListAsync(ct);
        var linkCount = await _db.FoodEventLinks.CountAsync(ct);

        var multiOccurrence = foods.Count(f => f.OccurrenceCount > 1);

        var worst = foods.Where(f => f.AvgSpike.HasValue && f.OccurrenceCount >= 2)
            .OrderByDescending(f => f.AvgSpike).FirstOrDefault();
        var safest = foods.Where(f => f.AvgSpike.HasValue && f.OccurrenceCount >= 2)
            .OrderBy(f => f.AvgSpike).FirstOrDefault();

        return new FoodStatsDto(
            TotalFoods: foods.Count,
            TotalLinks: linkCount,
            FoodsWithMultipleOccurrences: multiOccurrence,
            MostProblematicFood: worst?.Name,
            HighestAvgSpike: worst?.AvgSpike,
            SafestFood: safest?.Name,
            LowestAvgSpike: safest?.AvgSpike);
    }
}
