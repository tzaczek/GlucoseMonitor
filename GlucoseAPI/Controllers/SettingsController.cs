using GlucoseAPI.Application.Features.Settings;
using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly DatabaseBackupService _backupService;

    public SettingsController(IMediator mediator, DatabaseBackupService backupService)
    {
        _mediator = mediator;
        _backupService = backupService;
    }

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

    [HttpGet("backup")]
    public ActionResult GetBackupStatus()
    {
        return Ok(_backupService.GetStatus());
    }

    [HttpPost("backup")]
    public async Task<ActionResult> TriggerBackup(CancellationToken ct)
    {
        var result = await _backupService.TriggerBackupAsync(ct);
        return Ok(new { message = result });
    }

    [HttpPost("backup/restore")]
    public async Task<ActionResult> RestoreBackup([FromBody] RestoreBackupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.FileName))
            return BadRequest(new { message = "File name is required." });

        var result = await _backupService.RestoreFromBackupAsync(request.FileName, ct);
        var success = !result.Contains("failed", StringComparison.OrdinalIgnoreCase)
                   && !result.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
                   && !result.Contains("not found", StringComparison.OrdinalIgnoreCase)
                   && !result.Contains("already in progress", StringComparison.OrdinalIgnoreCase);
        return success ? Ok(new { message = result }) : BadRequest(new { message = result });
    }
}

public record RestoreBackupRequest(string FileName);
