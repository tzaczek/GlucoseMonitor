using GlucoseAPI.Application.Features.Settings;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SettingsController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetSettings(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLibreSettingsQuery(), ct);
        return Ok(result);
    }

    [HttpPut]
    public async Task<ActionResult> SaveSettings([FromBody] LibreSettingsDto dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new SaveLibreSettingsCommand(dto), ct);
        return result.Success
            ? Ok(new { message = result.Message })
            : BadRequest(result.Message);
    }

    [HttpGet("analysis")]
    public async Task<ActionResult> GetAnalysisSettings(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetAnalysisSettingsQuery(), ct);
        return Ok(result);
    }

    [HttpPut("analysis")]
    public async Task<ActionResult> SaveAnalysisSettings([FromBody] AnalysisSettingsDto dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new SaveAnalysisSettingsCommand(dto), ct);
        return result.Success
            ? Ok(new { message = result.Message })
            : BadRequest(result.Message);
    }

    [HttpPost("test")]
    public async Task<ActionResult> TestConnection([FromBody] LibreSettingsDto dto, CancellationToken ct)
    {
        var result = await _mediator.Send(new TestLibreLinkConnectionCommand(dto), ct);
        return Ok(result);
    }
}
