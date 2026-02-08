using GlucoseAPI.Application.Features.Notes;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotesController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<ActionResult> GetNotes(
        [FromQuery] string? folder = null,
        [FromQuery] string? search = null,
        [FromQuery] bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetNotesQuery(folder, search, includeDeleted), ct);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetNote(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNoteQuery(id), ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("folders")]
    public async Task<ActionResult> GetFolders(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNoteFoldersQuery(), ct);
        return Ok(result);
    }

    [HttpGet("status")]
    public async Task<ActionResult> GetStatus(CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNotesStatusQuery(), ct);
        return Ok(new
        {
            isAvailable = result.IsAvailable,
            noteCount = result.NoteCount,
            dataPath = result.DataPath
        });
    }

    [HttpGet("{id}/preview")]
    public async Task<IActionResult> GetPreview(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNotePreviewQuery(id), ct);
        if (result == null) return NotFound("No preview image available.");
        return File(result.Data, result.ContentType);
    }

    [HttpGet("{id}/media")]
    public async Task<ActionResult> GetMediaList(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNoteMediaListQuery(id), ct);
        return result == null ? NotFound() : Ok(result.Files);
    }

    [HttpGet("{id}/media/{fileName}")]
    public async Task<IActionResult> GetMediaFile(int id, string fileName, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetNoteMediaFileQuery(id, fileName), ct);
        if (result == null) return NotFound("Media file not found.");
        return File(result.Data, result.ContentType);
    }
}
