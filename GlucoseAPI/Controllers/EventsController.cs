using GlucoseAPI.Application.Features.Events;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IMediator _mediator;

    public EventsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetEvents([FromQuery] int? limit = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetEventsQuery(limit), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetEvent(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEventDetailQuery(id), ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("status")]
    public async Task<ActionResult> GetStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetEventsStatusQuery(), ct);
        return Ok(new
        {
            totalEvents = result.TotalEvents,
            processedEvents = result.ProcessedEvents,
            pendingEvents = result.PendingEvents
        });
    }

    [HttpPost("{id}/reprocess")]
    public async Task<ActionResult> Reprocess(int id, [FromBody] ReprocessRequest? request = null, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new ReprocessEventCommand(id, request?.ModelOverride), ct);

        if (!result.Found)
            return NotFound();

        return result.Success
            ? Ok(new { message = result.Message, analysis = result.Analysis })
            : StatusCode(500, new { message = result.Message });
    }
}

public record ReprocessRequest(string? ModelOverride = null);
