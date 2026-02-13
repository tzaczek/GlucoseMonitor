using GlucoseAPI.Application.Features.EventLogs;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventLogController : ControllerBase
{
    private readonly IMediator _mediator;
    public EventLogController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetEventLogs(
        [FromQuery] int? limit = 200,
        [FromQuery] int? offset = null,
        [FromQuery] string? level = null,
        [FromQuery] string? category = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEventLogsQuery(limit, offset, level, category, search, from, to), ct);
        return Ok(result);
    }
}
