using GlucoseAPI.Application.Features.DailySummaries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DailySummariesController : ControllerBase
{
    private readonly IMediator _mediator;

    public DailySummariesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetSummaries([FromQuery] int? limit = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetDailySummariesQuery(limit), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetSummary(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDailySummaryDetailQuery(id), ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("status")]
    public async Task<ActionResult> GetStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDailySummariesStatusQuery(), ct);
        return Ok(new
        {
            totalSummaries = result.TotalSummaries,
            processedSummaries = result.ProcessedSummaries,
            pendingSummaries = result.PendingSummaries
        });
    }

    [HttpGet("snapshots/{snapshotId}")]
    public async Task<ActionResult> GetSnapshot(int snapshotId, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSnapshotDetailQuery(snapshotId), ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost("trigger")]
    public async Task<ActionResult> TriggerGeneration(CancellationToken ct)
    {
        var result = await _mediator.Send(new TriggerDailySummaryCommand(), ct);
        return result.Success
            ? Ok(new { message = result.Message, processedCount = result.ProcessedCount })
            : StatusCode(500, new { message = result.Message });
    }
}
