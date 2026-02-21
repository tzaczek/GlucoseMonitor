using GlucoseAPI.Application.Features.Chat;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.AspNetCore.Mvc;
// ChatPeriod used in CreateSessionRequest

namespace GlucoseAPI.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly IMediator _mediator;

    public ChatController(IMediator mediator) => _mediator = mediator;

    // ── Sessions ────────────────────────────────────────────

    [HttpGet("sessions")]
    public async Task<ActionResult> ListSessions([FromQuery] int? limit = null, [FromQuery] int offset = 0, CancellationToken ct = default)
        => Ok(await _mediator.Send(new ListChatSessionsQuery(limit, offset), ct));

    [HttpGet("sessions/{id:int}")]
    public async Task<ActionResult<ChatSessionDetailDto>> GetSession(int id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetChatSessionQuery(id), ct);
        return result != null ? Ok(result) : NotFound();
    }

    [HttpPost("sessions")]
    public async Task<ActionResult> CreateSession([FromBody] CreateSessionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.InitialMessage))
            return BadRequest("Message is required.");

        var result = await _mediator.Send(new CreateChatSessionCommand(
            req.Title, req.PeriodStart, req.PeriodEnd, req.PeriodDescription,
            req.TemplateName, req.InitialMessage, req.Model, req.Periods), ct);

        return Ok(result);
    }

    [HttpPost("sessions/{id:int}/messages")]
    public async Task<ActionResult> SendMessage(int id, [FromBody] SendMessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest("Message content is required.");

        var result = await _mediator.Send(new SendChatMessageCommand(id, req.Content, req.ModelOverride), ct);
        return result.Success ? Ok(result) : BadRequest(result.Error);
    }

    [HttpDelete("sessions/{id:int}")]
    public async Task<ActionResult> DeleteSession(int id, CancellationToken ct)
    {
        var deleted = await _mediator.Send(new DeleteChatSessionCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpDelete("sessions")]
    public async Task<ActionResult> DeleteAllSessions(CancellationToken ct)
    {
        var count = await _mediator.Send(new DeleteAllChatSessionsCommand(), ct);
        return Ok(new { deleted = count });
    }

    // ── Templates ───────────────────────────────────────────

    [HttpGet("templates")]
    public async Task<ActionResult<List<ChatPromptTemplateDto>>> ListTemplates(CancellationToken ct)
        => Ok(await _mediator.Send(new ListChatTemplatesQuery(), ct));

    [HttpPost("templates")]
    public async Task<ActionResult<ChatPromptTemplateDto>> CreateTemplate(
        [FromBody] CreateTemplateRequest req, CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateChatTemplateCommand(
            req.Name, req.Category, req.SystemPrompt, req.UserPromptTemplate), ct);
        return Ok(result);
    }

    [HttpPut("templates/{id:int}")]
    public async Task<ActionResult> UpdateTemplate(int id, [FromBody] UpdateTemplateRequest req, CancellationToken ct)
    {
        var ok = await _mediator.Send(new UpdateChatTemplateCommand(
            id, req.Name, req.Category, req.SystemPrompt, req.UserPromptTemplate), ct);
        return ok ? Ok() : BadRequest("Template not found or is built-in.");
    }

    [HttpDelete("templates/{id:int}")]
    public async Task<ActionResult> DeleteTemplate(int id, CancellationToken ct)
    {
        var ok = await _mediator.Send(new DeleteChatTemplateCommand(id), ct);
        return ok ? NoContent() : BadRequest("Template not found or is built-in.");
    }
}

// ── Request DTOs ────────────────────────────────────────

public record CreateSessionRequest(
    string? Title,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    string? PeriodDescription,
    string? TemplateName,
    string InitialMessage,
    string? Model = null,
    List<ChatPeriod>? Periods = null);

public record SendMessageRequest(
    string Content,
    string? ModelOverride = null);

public record CreateTemplateRequest(
    string Name,
    string Category,
    string SystemPrompt,
    string UserPromptTemplate);

public record UpdateTemplateRequest(
    string Name,
    string Category,
    string SystemPrompt,
    string UserPromptTemplate);
