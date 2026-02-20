using GlucoseAPI.Application.Features.Glucose;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GlucoseController : ControllerBase
{
    private readonly IMediator _mediator;

    public GlucoseController(IMediator mediator) => _mediator = mediator;

    [HttpGet("latest")]
    public async Task<ActionResult> GetLatest(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLatestReadingQuery(), ct);
        return result == null ? NotFound("No readings available yet.") : Ok(result);
    }

    [HttpGet("history")]
    public async Task<ActionResult> GetHistory(
        [FromQuery] int hours = 24, [FromQuery] int? limit = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetGlucoseHistoryQuery(hours, limit), ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats([FromQuery] int hours = 24, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetGlucoseStatsQuery(hours), ct);
        return result == null
            ? NotFound("No readings available for the specified period.")
            : Ok(result);
    }

    [HttpGet("dates")]
    public async Task<ActionResult> GetDatesWithReadings(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDatesWithReadingsQuery(), ct);
        return Ok(result);
    }

    [HttpGet("range")]
    public async Task<ActionResult> GetRange(
        [FromQuery] DateTime start, [FromQuery] DateTime end, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetGlucoseRangeQuery(start.ToUniversalTime(), end.ToUniversalTime()), ct);
        return Ok(result);
    }
}
