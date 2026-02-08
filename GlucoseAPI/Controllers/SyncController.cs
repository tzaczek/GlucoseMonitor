using GlucoseAPI.Application.Features.Sync;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly IMediator _mediator;

    public SyncController(IMediator mediator) => _mediator = mediator;

    [HttpPost("trigger")]
    public async Task<ActionResult> TriggerSync(CancellationToken ct)
    {
        var result = await _mediator.Send(new TriggerFullSyncCommand(), ct);
        return Ok(new { message = result.Message, results = result.Results });
    }

    [HttpPost("glucose")]
    public async Task<ActionResult> TriggerGlucoseSync(CancellationToken ct)
    {
        var result = await _mediator.Send(new TriggerGlucoseSyncCommand(), ct);
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : StatusCode(500, new { success = false, message = result.Message });
    }

    [HttpPost("notes")]
    public async Task<ActionResult> TriggerNotesSync(CancellationToken ct)
    {
        var result = await _mediator.Send(new TriggerNotesSyncCommand(), ct);
        return result.Success
            ? Ok(new { success = true, message = result.Message })
            : StatusCode(500, new { success = false, message = result.Message });
    }
}
