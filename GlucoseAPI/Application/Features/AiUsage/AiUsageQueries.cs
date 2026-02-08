using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.AiUsage;

// ── GetAiUsageLogs ────────────────────────────────────────────

public record GetAiUsageLogsQuery(int? Limit, DateTime? From, DateTime? To)
    : IRequest<List<AiUsageLogDto>>;

public record AiUsageLogDto(
    int Id, int? GlucoseEventId, string Model,
    int InputTokens, int OutputTokens, int TotalTokens, double Cost,
    string? Reason, bool Success, int? HttpStatusCode,
    string? FinishReason, DateTime CalledAt, int? DurationMs);

public class GetAiUsageLogsHandler : IRequestHandler<GetAiUsageLogsQuery, List<AiUsageLogDto>>
{
    private readonly GlucoseDbContext _db;

    public GetAiUsageLogsHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<AiUsageLogDto>> Handle(GetAiUsageLogsQuery request, CancellationToken ct)
    {
        var query = _db.AiUsageLogs.AsQueryable();

        if (request.From.HasValue)
            query = query.Where(l => l.CalledAt >= request.From.Value.ToUniversalTime());
        if (request.To.HasValue)
            query = query.Where(l => l.CalledAt <= request.To.Value.ToUniversalTime());

        query = query.OrderByDescending(l => l.CalledAt);

        if (request.Limit is > 0)
            query = query.Take(request.Limit.Value);

        var logs = await query.ToListAsync(ct);

        return logs.Select(l => new AiUsageLogDto(
            l.Id, l.GlucoseEventId, l.Model,
            l.InputTokens, l.OutputTokens, l.TotalTokens,
            Math.Round(AiCostCalculator.ComputeCost(l.Model, l.InputTokens, l.OutputTokens), 6),
            l.Reason, l.Success, l.HttpStatusCode,
            l.FinishReason, l.CalledAt, l.DurationMs
        )).ToList();
    }
}

// ── GetAiUsageSummary ─────────────────────────────────────────

public record GetAiUsageSummaryQuery(DateTime? From, DateTime? To) : IRequest<AiUsageSummaryDto>;

public class AiUsageSummaryDto
{
    public int TotalCalls { get; init; }
    public int SuccessfulCalls { get; init; }
    public int FailedCalls { get; init; }
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public double TotalCost { get; init; }
    public double AvgInputTokens { get; init; }
    public double AvgOutputTokens { get; init; }
    public double AvgDurationMs { get; init; }
    public double AvgCostPerCall { get; init; }
    public List<ModelBreakdownDto> ModelBreakdown { get; init; } = new();
    public List<DailyUsageDto> DailyUsage { get; init; } = new();
}

public record ModelBreakdownDto(
    string Model, int Calls, int SuccessfulCalls, int FailedCalls,
    long InputTokens, long OutputTokens, long TotalTokens,
    double Cost, double AvgInputTokens, double AvgOutputTokens,
    double AvgDurationMs, double AvgCostPerCall);

public record DailyUsageDto(
    string Date, int Calls, int SuccessfulCalls,
    long InputTokens, long OutputTokens, long TotalTokens, double Cost);

public class GetAiUsageSummaryHandler : IRequestHandler<GetAiUsageSummaryQuery, AiUsageSummaryDto>
{
    private readonly GlucoseDbContext _db;

    public GetAiUsageSummaryHandler(GlucoseDbContext db) => _db = db;

    public async Task<AiUsageSummaryDto> Handle(GetAiUsageSummaryQuery request, CancellationToken ct)
    {
        var query = _db.AiUsageLogs.AsQueryable();

        if (request.From.HasValue)
            query = query.Where(l => l.CalledAt >= request.From.Value.ToUniversalTime());
        if (request.To.HasValue)
            query = query.Where(l => l.CalledAt <= request.To.Value.ToUniversalTime());

        var allLogs = await query.ToListAsync(ct);

        if (allLogs.Count == 0)
            return new AiUsageSummaryDto();

        var successLogs = allLogs.Where(l => l.Success).ToList();
        var totalCost = allLogs.Sum(l =>
            AiCostCalculator.ComputeCost(l.Model, l.InputTokens, l.OutputTokens));

        var modelBreakdown = allLogs
            .GroupBy(l => l.Model)
            .Select(g =>
            {
                var modelCost = g.Sum(l =>
                    AiCostCalculator.ComputeCost(l.Model, l.InputTokens, l.OutputTokens));
                return new ModelBreakdownDto(
                    g.Key, g.Count(),
                    g.Count(l => l.Success), g.Count(l => !l.Success),
                    g.Sum(l => l.InputTokens), g.Sum(l => l.OutputTokens),
                    g.Sum(l => l.TotalTokens), Math.Round(modelCost, 6),
                    g.Average(l => (double)l.InputTokens),
                    g.Average(l => (double)l.OutputTokens),
                    g.Where(l => l.DurationMs.HasValue)
                        .Select(l => (double)l.DurationMs!.Value)
                        .DefaultIfEmpty(0).Average(),
                    g.Count() > 0 ? Math.Round(modelCost / g.Count(), 6) : 0.0);
            })
            .OrderByDescending(m => m.Calls)
            .ToList();

        var dailyUsage = allLogs
            .GroupBy(l => l.CalledAt.Date)
            .Select(g => new DailyUsageDto(
                g.Key.ToString("yyyy-MM-dd"), g.Count(),
                g.Count(l => l.Success),
                g.Sum(l => l.InputTokens), g.Sum(l => l.OutputTokens),
                g.Sum(l => l.TotalTokens),
                Math.Round(g.Sum(l =>
                    AiCostCalculator.ComputeCost(l.Model, l.InputTokens, l.OutputTokens)), 6)))
            .OrderBy(d => d.Date)
            .ToList();

        return new AiUsageSummaryDto
        {
            TotalCalls = allLogs.Count,
            SuccessfulCalls = successLogs.Count,
            FailedCalls = allLogs.Count - successLogs.Count,
            TotalInputTokens = allLogs.Sum(l => l.InputTokens),
            TotalOutputTokens = allLogs.Sum(l => l.OutputTokens),
            TotalTokens = allLogs.Sum(l => l.TotalTokens),
            TotalCost = Math.Round(totalCost, 6),
            AvgInputTokens = successLogs.Count > 0
                ? Math.Round(successLogs.Average(l => (double)l.InputTokens), 1) : 0,
            AvgOutputTokens = successLogs.Count > 0
                ? Math.Round(successLogs.Average(l => (double)l.OutputTokens), 1) : 0,
            AvgDurationMs = successLogs
                .Where(l => l.DurationMs.HasValue)
                .Select(l => (double)l.DurationMs!.Value)
                .DefaultIfEmpty(0).Average(),
            AvgCostPerCall = allLogs.Count > 0
                ? Math.Round(totalCost / allLogs.Count, 6) : 0.0,
            ModelBreakdown = modelBreakdown,
            DailyUsage = dailyUsage
        };
    }
}

// ── GetAiUsagePricing ─────────────────────────────────────────

public record GetAiUsagePricingQuery : IRequest<List<AiUsagePricingDto>>;

public record AiUsagePricingDto(string Model, double InputPer1M, double OutputPer1M);

public class GetAiUsagePricingHandler : IRequestHandler<GetAiUsagePricingQuery, List<AiUsagePricingDto>>
{
    public Task<List<AiUsagePricingDto>> Handle(GetAiUsagePricingQuery request, CancellationToken ct)
    {
        var pricing = AiCostCalculator.GetPricingTable()
            .Select(kv => new AiUsagePricingDto(kv.Key, kv.Value.InputPer1M, kv.Value.OutputPer1M))
            .ToList();
        return Task.FromResult(pricing);
    }
}
