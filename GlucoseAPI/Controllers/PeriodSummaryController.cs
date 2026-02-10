using GlucoseAPI.Application.Features.PeriodSummaries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PeriodSummaryController : ControllerBase
{
    private readonly IMediator _mediator;
    public PeriodSummaryController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetPeriodSummaries([FromQuery] int? limit = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPeriodSummariesQuery(limit), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetPeriodSummary(int id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetPeriodSummaryDetailQuery(id), ct);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult> CreatePeriodSummary([FromBody] CreatePeriodSummaryRequest request, CancellationToken ct = default)
    {
        var command = new CreatePeriodSummaryCommand(
            request.Name,
            request.PeriodStart,
            request.PeriodEnd
        );
        var result = await _mediator.Send(command, ct);
        if (!result.Success) return BadRequest(new { result.Message });
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeletePeriodSummary(int id, CancellationToken ct = default)
    {
        var deleted = await _mediator.Send(new DeletePeriodSummaryCommand(id), ct);
        if (!deleted) return NotFound();
        return Ok(new { message = "Period summary deleted." });
    }
}

public record CreatePeriodSummaryRequest(
    string? Name,
    DateTime PeriodStart,
    DateTime PeriodEnd
);
