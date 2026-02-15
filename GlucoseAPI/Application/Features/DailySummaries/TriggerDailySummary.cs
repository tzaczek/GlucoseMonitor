using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.DailySummaries;

public record TriggerDailySummaryCommand(string? ModelOverride = null) : IRequest<TriggerDailySummaryResult>;

public record TriggerDailySummaryResult(bool Success, int ProcessedCount, string Message);

public class TriggerDailySummaryHandler
    : IRequestHandler<TriggerDailySummaryCommand, TriggerDailySummaryResult>
{
    private readonly DailySummaryService _dailySummaryService;
    private readonly ILogger<TriggerDailySummaryHandler> _logger;

    public TriggerDailySummaryHandler(DailySummaryService dailySummaryService, ILogger<TriggerDailySummaryHandler> logger)
    {
        _dailySummaryService = dailySummaryService;
        _logger = logger;
    }

    public async Task<TriggerDailySummaryResult> Handle(TriggerDailySummaryCommand request, CancellationToken ct)
    {
        _logger.LogInformation("Manual daily summary generation requested via MediatR.");

        try
        {
            var count = await _dailySummaryService.TriggerGenerationAsync(ct, request.ModelOverride);
            var message = count > 0
                ? $"Successfully processed {count} daily summary(s)."
                : "No missing daily summaries to process.";
            return new TriggerDailySummaryResult(true, count, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger daily summary generation.");
            return new TriggerDailySummaryResult(false, 0, $"Failed to generate daily summaries: {ex.Message}");
        }
    }
}
