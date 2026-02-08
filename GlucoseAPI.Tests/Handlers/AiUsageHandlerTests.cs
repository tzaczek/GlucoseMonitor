using FluentAssertions;
using GlucoseAPI.Application.Features.AiUsage;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GlucoseAPI.Tests.Handlers;

public class AiUsageHandlerTests : IDisposable
{
    private readonly GlucoseDbContext _db;

    public AiUsageHandlerTests()
    {
        var options = new DbContextOptionsBuilder<GlucoseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GlucoseDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    // ── GetAiUsageLogs ───────────────────────────────────────

    [Fact]
    public async Task GetAiUsageLogs_EmptyDb_ReturnsEmpty()
    {
        var handler = new GetAiUsageLogsHandler(_db);
        var result = await handler.Handle(new GetAiUsageLogsQuery(null, null, null), CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAiUsageLogs_CalculatesCostPerEntry()
    {
        _db.AiUsageLogs.Add(new AiUsageLog
        {
            Model = "gpt-4o-mini",
            InputTokens = 1000,
            OutputTokens = 500,
            TotalTokens = 1500,
            CalledAt = DateTime.UtcNow,
            Success = true
        });
        await _db.SaveChangesAsync();

        var handler = new GetAiUsageLogsHandler(_db);
        var result = await handler.Handle(new GetAiUsageLogsQuery(null, null, null), CancellationToken.None);

        result.Should().HaveCount(1);
        var expectedCost = AiCostCalculator.ComputeCost("gpt-4o-mini", 1000, 500);
        result[0].Cost.Should().BeApproximately(expectedCost, 0.000001);
    }

    [Fact]
    public async Task GetAiUsageLogs_RespectsLimit()
    {
        var baseDate = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            _db.AiUsageLogs.Add(new AiUsageLog
            {
                Model = "gpt-4o-mini",
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                CalledAt = baseDate.AddDays(-i),
                Success = true
            });
        }
        await _db.SaveChangesAsync();

        var handler = new GetAiUsageLogsHandler(_db);
        var limited = await handler.Handle(new GetAiUsageLogsQuery(2, null, null), CancellationToken.None);
        limited.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAiUsageLogs_RespectsDateRange()
    {
        var baseDate = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++)
        {
            _db.AiUsageLogs.Add(new AiUsageLog
            {
                Model = "gpt-4o-mini",
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                CalledAt = baseDate.AddDays(-i),
                Success = true
            });
        }
        await _db.SaveChangesAsync();

        var handler = new GetAiUsageLogsHandler(_db);
        var ranged = await handler.Handle(
            new GetAiUsageLogsQuery(null, baseDate.AddDays(-2), baseDate),
            CancellationToken.None);
        ranged.Should().HaveCount(3); // baseDate, -1, -2
    }

    // ── GetAiUsageSummary ────────────────────────────────────

    [Fact]
    public async Task GetAiUsageSummary_EmptyDb_ReturnsZeroSummary()
    {
        var handler = new GetAiUsageSummaryHandler(_db);
        var result = await handler.Handle(new GetAiUsageSummaryQuery(null, null), CancellationToken.None);

        result.TotalCalls.Should().Be(0);
        result.TotalCost.Should().Be(0);
        result.ModelBreakdown.Should().BeEmpty();
        result.DailyUsage.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAiUsageSummary_CalculatesTotalsAndBreakdown()
    {
        _db.AiUsageLogs.AddRange(
            new AiUsageLog
            {
                Model = "gpt-4o-mini",
                InputTokens = 1000, OutputTokens = 500, TotalTokens = 1500,
                CalledAt = DateTime.UtcNow, Success = true, DurationMs = 1000
            },
            new AiUsageLog
            {
                Model = "gpt-4o-mini",
                InputTokens = 2000, OutputTokens = 1000, TotalTokens = 3000,
                CalledAt = DateTime.UtcNow, Success = true, DurationMs = 2000
            },
            new AiUsageLog
            {
                Model = "gpt-4o",
                InputTokens = 500, OutputTokens = 200, TotalTokens = 700,
                CalledAt = DateTime.UtcNow, Success = false, DurationMs = 500
            });
        await _db.SaveChangesAsync();

        var handler = new GetAiUsageSummaryHandler(_db);
        var result = await handler.Handle(new GetAiUsageSummaryQuery(null, null), CancellationToken.None);

        result.TotalCalls.Should().Be(3);
        result.SuccessfulCalls.Should().Be(2);
        result.FailedCalls.Should().Be(1);
        result.TotalInputTokens.Should().Be(3500);
        result.TotalOutputTokens.Should().Be(1700);
        result.TotalCost.Should().BeGreaterThan(0);

        result.ModelBreakdown.Should().HaveCount(2);
        result.ModelBreakdown.First(m => m.Model == "gpt-4o-mini").Calls.Should().Be(2);
        result.ModelBreakdown.First(m => m.Model == "gpt-4o").Calls.Should().Be(1);

        result.DailyUsage.Should().HaveCount(1);
        result.DailyUsage[0].Calls.Should().Be(3);
    }

    // ── GetAiUsagePricing ────────────────────────────────────

    [Fact]
    public async Task GetAiUsagePricing_ReturnsKnownModels()
    {
        var handler = new GetAiUsagePricingHandler();
        var result = await handler.Handle(new GetAiUsagePricingQuery(), CancellationToken.None);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Model == "gpt-4o-mini");
        result.Should().Contain(p => p.Model == "gpt-4o");
        result.All(p => p.InputPer1M > 0 && p.OutputPer1M > 0).Should().BeTrue();
    }
}
