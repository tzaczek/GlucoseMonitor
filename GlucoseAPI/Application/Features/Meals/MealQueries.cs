using GlucoseAPI.Application.Common;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Meals;

// ── DTOs ─────────────────────────────────────────────────────

public class MealSummaryDto
{
    public int Id { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteTitleEn { get; set; }
    public string? NoteContentPreview { get; set; }
    public string? NoteContentPreviewEn { get; set; }
    public DateTime EventTimestamp { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseSpike { get; set; }
    public bool IsProcessed { get; set; }
    public string? AiClassification { get; set; }
    public List<MealFoodDto> Foods { get; set; } = new();
}

public class MealDetailDto
{
    public int Id { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteTitleEn { get; set; }
    public string? NoteContent { get; set; }
    public string? NoteContentEn { get; set; }
    public DateTime EventTimestamp { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseSpike { get; set; }
    public DateTime? PeakTime { get; set; }
    public string? AiAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public string? AiModel { get; set; }
    public bool IsProcessed { get; set; }
    public List<MealFoodDto> Foods { get; set; } = new();
}

public class MealFoodDto
{
    public int FoodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Category { get; set; }
    public double? Spike { get; set; }
    public string? AiClassification { get; set; }
}

public class MealComparisonDto
{
    public List<MealDetailDto> Meals { get; set; } = new();
}

public class MealStatsDto
{
    public int TotalMeals { get; set; }
    public int AnalyzedMeals { get; set; }
    public int GreenMeals { get; set; }
    public int YellowMeals { get; set; }
    public int RedMeals { get; set; }
    public double? AvgSpike { get; set; }
    public double? WorstSpike { get; set; }
    public double? BestSpike { get; set; }
}

// ── List meals ───────────────────────────────────────────────

public record GetMealsQuery(
    string? Search = null,
    string? SortBy = null,
    bool Descending = true,
    string? Classification = null,
    int? Limit = null,
    int Offset = 0
) : IRequest<PagedResult<MealSummaryDto>>;

public class GetMealsHandler : IRequestHandler<GetMealsQuery, PagedResult<MealSummaryDto>>
{
    private readonly GlucoseDbContext _db;
    public GetMealsHandler(GlucoseDbContext db) => _db = db;

    public async Task<PagedResult<MealSummaryDto>> Handle(GetMealsQuery request, CancellationToken ct)
    {
        var query = _db.GlucoseEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.ToLower();
            query = query.Where(e =>
                e.NoteTitle.ToLower().Contains(s) ||
                (e.NoteTitleEn != null && e.NoteTitleEn.ToLower().Contains(s)) ||
                (e.NoteContent != null && e.NoteContent.ToLower().Contains(s)) ||
                (e.NoteContentEn != null && e.NoteContentEn.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(request.Classification))
            query = query.Where(e => e.AiClassification == request.Classification);

        query = request.SortBy?.ToLower() switch
        {
            "spike" => request.Descending
                ? query.OrderByDescending(e => e.GlucoseSpike)
                : query.OrderBy(e => e.GlucoseSpike),
            "glucose" => request.Descending
                ? query.OrderByDescending(e => e.GlucoseAtEvent)
                : query.OrderBy(e => e.GlucoseAtEvent),
            "name" => request.Descending
                ? query.OrderByDescending(e => e.NoteTitle)
                : query.OrderBy(e => e.NoteTitle),
            _ => request.Descending
                ? query.OrderByDescending(e => e.EventTimestamp)
                : query.OrderBy(e => e.EventTimestamp)
        };

        var totalCount = await query.CountAsync(ct);

        query = query.Skip(request.Offset);

        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var events = await query.ToListAsync(ct);
        var eventIds = events.Select(e => e.Id).ToList();

        var foodLinks = await _db.FoodEventLinks
            .Where(l => eventIds.Contains(l.GlucoseEventId))
            .Include(l => l.FoodItem)
            .ToListAsync(ct);

        var foodsByEvent = foodLinks
            .GroupBy(l => l.GlucoseEventId)
            .ToDictionary(g => g.Key, g => g.Select(l => new MealFoodDto
            {
                FoodId = l.FoodItemId,
                Name = l.FoodItem?.Name ?? "",
                NameEn = l.FoodItem?.NameEn,
                Category = l.FoodItem?.Category,
                Spike = l.Spike,
                AiClassification = l.AiClassification
            }).ToList());

        var items = events.Select(e => new MealSummaryDto
        {
            Id = e.Id,
            NoteTitle = e.NoteTitle,
            NoteTitleEn = e.NoteTitleEn,
            NoteContentPreview = e.NoteContent != null && e.NoteContent.Length > 120
                ? e.NoteContent[..120] + "…" : e.NoteContent,
            NoteContentPreviewEn = e.NoteContentEn != null && e.NoteContentEn.Length > 120
                ? e.NoteContentEn[..120] + "…" : e.NoteContentEn,
            EventTimestamp = DateTime.SpecifyKind(e.EventTimestamp, DateTimeKind.Utc),
            ReadingCount = e.ReadingCount,
            GlucoseAtEvent = e.GlucoseAtEvent,
            GlucoseMin = e.GlucoseMin,
            GlucoseMax = e.GlucoseMax,
            GlucoseAvg = e.GlucoseAvg,
            GlucoseSpike = e.GlucoseSpike,
            IsProcessed = e.IsProcessed,
            AiClassification = e.AiClassification,
            Foods = foodsByEvent.GetValueOrDefault(e.Id, new List<MealFoodDto>())
        }).ToList();
        return new PagedResult<MealSummaryDto>(items, totalCount);
    }
}

// ── Meal detail ──────────────────────────────────────────────

public record GetMealDetailQuery(int Id) : IRequest<MealDetailDto?>;

public class GetMealDetailHandler : IRequestHandler<GetMealDetailQuery, MealDetailDto?>
{
    private readonly GlucoseDbContext _db;
    public GetMealDetailHandler(GlucoseDbContext db) => _db = db;

    public async Task<MealDetailDto?> Handle(GetMealDetailQuery request, CancellationToken ct)
    {
        var evt = await _db.GlucoseEvents.FindAsync(new object[] { request.Id }, ct);
        if (evt == null) return null;

        var foods = await _db.FoodEventLinks
            .Where(l => l.GlucoseEventId == evt.Id)
            .Include(l => l.FoodItem)
            .Select(l => new MealFoodDto
            {
                FoodId = l.FoodItemId,
                Name = l.FoodItem!.Name,
                NameEn = l.FoodItem.NameEn,
                Category = l.FoodItem.Category,
                Spike = l.Spike,
                AiClassification = l.AiClassification
            }).ToListAsync(ct);

        return new MealDetailDto
        {
            Id = evt.Id,
            NoteTitle = evt.NoteTitle,
            NoteTitleEn = evt.NoteTitleEn,
            NoteContent = evt.NoteContent,
            NoteContentEn = evt.NoteContentEn,
            EventTimestamp = DateTime.SpecifyKind(evt.EventTimestamp, DateTimeKind.Utc),
            PeriodStart = DateTime.SpecifyKind(evt.PeriodStart, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(evt.PeriodEnd, DateTimeKind.Utc),
            ReadingCount = evt.ReadingCount,
            GlucoseAtEvent = evt.GlucoseAtEvent,
            GlucoseMin = evt.GlucoseMin,
            GlucoseMax = evt.GlucoseMax,
            GlucoseAvg = evt.GlucoseAvg,
            GlucoseSpike = evt.GlucoseSpike,
            PeakTime = evt.PeakTime.HasValue
                ? DateTime.SpecifyKind(evt.PeakTime.Value, DateTimeKind.Utc) : null,
            AiAnalysis = evt.AiAnalysis,
            AiClassification = evt.AiClassification,
            AiModel = evt.AiModel,
            IsProcessed = evt.IsProcessed,
            Foods = foods
        };
    }
}

// ── Compare meals ────────────────────────────────────────────

public record CompareMealsQuery(List<int> Ids) : IRequest<MealComparisonDto>;

public class CompareMealsHandler : IRequestHandler<CompareMealsQuery, MealComparisonDto>
{
    private readonly GlucoseDbContext _db;
    public CompareMealsHandler(GlucoseDbContext db) => _db = db;

    public async Task<MealComparisonDto> Handle(CompareMealsQuery request, CancellationToken ct)
    {
        var events = await _db.GlucoseEvents
            .Where(e => request.Ids.Contains(e.Id))
            .ToListAsync(ct);

        var eventIds = events.Select(e => e.Id).ToList();

        var foodLinks = await _db.FoodEventLinks
            .Where(l => eventIds.Contains(l.GlucoseEventId))
            .Include(l => l.FoodItem)
            .ToListAsync(ct);

        var foodsByEvent = foodLinks
            .GroupBy(l => l.GlucoseEventId)
            .ToDictionary(g => g.Key, g => g.Select(l => new MealFoodDto
            {
                FoodId = l.FoodItemId,
                Name = l.FoodItem?.Name ?? "",
                NameEn = l.FoodItem?.NameEn,
                Category = l.FoodItem?.Category,
                Spike = l.Spike,
                AiClassification = l.AiClassification
            }).ToList());

        var meals = events.Select(evt => new MealDetailDto
        {
            Id = evt.Id,
            NoteTitle = evt.NoteTitle,
            NoteTitleEn = evt.NoteTitleEn,
            NoteContent = evt.NoteContent,
            NoteContentEn = evt.NoteContentEn,
            EventTimestamp = DateTime.SpecifyKind(evt.EventTimestamp, DateTimeKind.Utc),
            PeriodStart = DateTime.SpecifyKind(evt.PeriodStart, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(evt.PeriodEnd, DateTimeKind.Utc),
            ReadingCount = evt.ReadingCount,
            GlucoseAtEvent = evt.GlucoseAtEvent,
            GlucoseMin = evt.GlucoseMin,
            GlucoseMax = evt.GlucoseMax,
            GlucoseAvg = evt.GlucoseAvg,
            GlucoseSpike = evt.GlucoseSpike,
            PeakTime = evt.PeakTime.HasValue
                ? DateTime.SpecifyKind(evt.PeakTime.Value, DateTimeKind.Utc) : null,
            AiAnalysis = evt.AiAnalysis,
            AiClassification = evt.AiClassification,
            AiModel = evt.AiModel,
            IsProcessed = evt.IsProcessed,
            Foods = foodsByEvent.GetValueOrDefault(evt.Id, new List<MealFoodDto>())
        }).OrderByDescending(m => m.EventTimestamp).ToList();

        return new MealComparisonDto { Meals = meals };
    }
}

// ── Meal stats ───────────────────────────────────────────────

public record GetMealStatsQuery() : IRequest<MealStatsDto>;

public class GetMealStatsHandler : IRequestHandler<GetMealStatsQuery, MealStatsDto>
{
    private readonly GlucoseDbContext _db;
    public GetMealStatsHandler(GlucoseDbContext db) => _db = db;

    public async Task<MealStatsDto> Handle(GetMealStatsQuery request, CancellationToken ct)
    {
        var events = await _db.GlucoseEvents.ToListAsync(ct);

        var analyzed = events.Where(e => e.IsProcessed).ToList();
        var withSpike = analyzed.Where(e => e.GlucoseSpike.HasValue).ToList();

        return new MealStatsDto
        {
            TotalMeals = events.Count,
            AnalyzedMeals = analyzed.Count,
            GreenMeals = analyzed.Count(e => e.AiClassification == "green"),
            YellowMeals = analyzed.Count(e => e.AiClassification == "yellow"),
            RedMeals = analyzed.Count(e => e.AiClassification == "red"),
            AvgSpike = withSpike.Count > 0 ? Math.Round(withSpike.Average(e => e.GlucoseSpike!.Value), 1) : null,
            WorstSpike = withSpike.Count > 0 ? withSpike.Max(e => e.GlucoseSpike) : null,
            BestSpike = withSpike.Count > 0 ? withSpike.Min(e => e.GlucoseSpike) : null
        };
    }
}
