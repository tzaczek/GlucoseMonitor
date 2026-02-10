using GlucoseAPI.Data;
using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.Comparisons;

public record CreateComparisonCommand(
    string? Name,
    DateTime PeriodAStart,
    DateTime PeriodAEnd,
    string? PeriodALabel,
    DateTime PeriodBStart,
    DateTime PeriodBEnd,
    string? PeriodBLabel
) : IRequest<CreateComparisonResult>;

public record CreateComparisonResult(bool Success, int? Id, string Message);

public class CreateComparisonHandler : IRequestHandler<CreateComparisonCommand, CreateComparisonResult>
{
    private readonly GlucoseDbContext _db;
    private readonly ComparisonService _comparisonService;

    public CreateComparisonHandler(GlucoseDbContext db, ComparisonService comparisonService)
    {
        _db = db;
        _comparisonService = comparisonService;
    }

    public async Task<CreateComparisonResult> Handle(CreateComparisonCommand request, CancellationToken ct)
    {
        if (request.PeriodAStart >= request.PeriodAEnd)
            return new CreateComparisonResult(false, null, "Period A start must be before end.");
        if (request.PeriodBStart >= request.PeriodBEnd)
            return new CreateComparisonResult(false, null, "Period B start must be before end.");

        var comparison = new GlucoseComparison
        {
            Name = request.Name,
            PeriodAStart = DateTime.SpecifyKind(request.PeriodAStart, DateTimeKind.Utc),
            PeriodAEnd = DateTime.SpecifyKind(request.PeriodAEnd, DateTimeKind.Utc),
            PeriodALabel = request.PeriodALabel,
            PeriodBStart = DateTime.SpecifyKind(request.PeriodBStart, DateTimeKind.Utc),
            PeriodBEnd = DateTime.SpecifyKind(request.PeriodBEnd, DateTimeKind.Utc),
            PeriodBLabel = request.PeriodBLabel,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.GlucoseComparisons.Add(comparison);
        await _db.SaveChangesAsync(ct);

        // Enqueue for background processing
        _comparisonService.Enqueue(comparison.Id);

        return new CreateComparisonResult(true, comparison.Id, "Comparison queued for processing.");
    }
}
