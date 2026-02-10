using GlucoseAPI.Data;
using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.PeriodSummaries;

public record CreatePeriodSummaryCommand(
    string? Name,
    DateTime PeriodStart,
    DateTime PeriodEnd
) : IRequest<CreatePeriodSummaryResult>;

public record CreatePeriodSummaryResult(bool Success, int? Id, string Message);

public class CreatePeriodSummaryHandler : IRequestHandler<CreatePeriodSummaryCommand, CreatePeriodSummaryResult>
{
    private readonly GlucoseDbContext _db;
    private readonly PeriodSummaryService _periodSummaryService;

    public CreatePeriodSummaryHandler(GlucoseDbContext db, PeriodSummaryService periodSummaryService)
    {
        _db = db;
        _periodSummaryService = periodSummaryService;
    }

    public async Task<CreatePeriodSummaryResult> Handle(CreatePeriodSummaryCommand request, CancellationToken ct)
    {
        if (request.PeriodStart >= request.PeriodEnd)
            return new CreatePeriodSummaryResult(false, null, "Period start must be before end.");

        var summary = new PeriodSummary
        {
            Name = request.Name,
            PeriodStart = DateTime.SpecifyKind(request.PeriodStart, DateTimeKind.Utc),
            PeriodEnd = DateTime.SpecifyKind(request.PeriodEnd, DateTimeKind.Utc),
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.PeriodSummaries.Add(summary);
        await _db.SaveChangesAsync(ct);

        // Enqueue for background processing
        _periodSummaryService.Enqueue(summary.Id);

        return new CreatePeriodSummaryResult(true, summary.Id, "Period summary queued for processing.");
    }
}
