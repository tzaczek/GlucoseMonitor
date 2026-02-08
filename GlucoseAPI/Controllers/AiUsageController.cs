using GlucoseAPI.Application.Features.AiUsage;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiUsageController : ControllerBase
{
    private readonly IMediator _mediator;

    public AiUsageController(IMediator mediator) => _mediator = mediator;

    [HttpGet("logs")]
    public async Task<ActionResult> GetLogs(
        [FromQuery] int? limit,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAiUsageLogsQuery(limit, from, to), ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> GetSummary(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetAiUsageSummaryQuery(from, to), ct);
        return Ok(result);
    }

    [HttpGet("pricing")]
    public async Task<ActionResult> GetPricing(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAiUsagePricingQuery(), ct);
        return Ok(result);
    }
}
