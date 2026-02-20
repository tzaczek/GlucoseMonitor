using GlucoseAPI.Data;
using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Chat;

// ── CreateChatSession ────────────────────────────────────

public record CreateChatSessionCommand(
    string? Title,
    DateTime? PeriodStart,
    DateTime? PeriodEnd,
    string? PeriodDescription,
    string? TemplateName,
    string InitialMessage,
    string? Model,
    List<ChatPeriod>? Periods = null
) : IRequest<CreateChatSessionResult>;

public record CreateChatSessionResult(int SessionId, int UserMessageId, int AssistantMessageId);

public class CreateChatSessionHandler : IRequestHandler<CreateChatSessionCommand, CreateChatSessionResult>
{
    private readonly GlucoseDbContext _db;
    private readonly ChatService _chatService;

    public CreateChatSessionHandler(GlucoseDbContext db, ChatService chatService)
    {
        _db = db;
        _chatService = chatService;
    }

    public async Task<CreateChatSessionResult> Handle(CreateChatSessionCommand request, CancellationToken ct)
    {
        var title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title
            : request.InitialMessage.Length > 80
                ? request.InitialMessage[..80] + "…"
                : request.InitialMessage;

        var session = new ChatSession
        {
            Title = title,
            PeriodStart = request.PeriodStart,
            PeriodEnd = request.PeriodEnd,
            PeriodDescription = request.PeriodDescription,
            TemplateName = request.TemplateName,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        if (request.Periods is { Count: > 0 })
        {
            session.Periods = request.Periods;
            session.PeriodStart = request.Periods.Min(p => p.Start);
            session.PeriodEnd = request.Periods.Max(p => p.End);
        }

        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var userMsg = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "user",
            Content = request.InitialMessage,
            Status = "completed",
            CreatedAt = DateTime.UtcNow,
        };

        var assistantMsg = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "assistant",
            Content = "",
            Status = "processing",
            CreatedAt = DateTime.UtcNow,
        };

        _db.ChatMessages.Add(userMsg);
        _db.ChatMessages.Add(assistantMsg);
        await _db.SaveChangesAsync(ct);

        _chatService.Enqueue(assistantMsg.Id, request.Model);

        return new CreateChatSessionResult(session.Id, userMsg.Id, assistantMsg.Id);
    }
}

// ── SendChatMessage ──────────────────────────────────────

public record SendChatMessageCommand(
    int SessionId,
    string Content,
    string? ModelOverride
) : IRequest<SendChatMessageResult>;

public record SendChatMessageResult(bool Success, int UserMessageId, int AssistantMessageId, string? Error = null);

public class SendChatMessageHandler : IRequestHandler<SendChatMessageCommand, SendChatMessageResult>
{
    private readonly GlucoseDbContext _db;
    private readonly ChatService _chatService;

    public SendChatMessageHandler(GlucoseDbContext db, ChatService chatService)
    {
        _db = db;
        _chatService = chatService;
    }

    public async Task<SendChatMessageResult> Handle(SendChatMessageCommand request, CancellationToken ct)
    {
        var session = await _db.ChatSessions.FindAsync(new object[] { request.SessionId }, ct);
        if (session == null)
            return new SendChatMessageResult(false, 0, 0, "Session not found.");

        // Don't allow sending if another message is still processing
        var hasProcessing = await _db.ChatMessages
            .AnyAsync(m => m.ChatSessionId == request.SessionId && m.Status == "processing", ct);
        if (hasProcessing)
            return new SendChatMessageResult(false, 0, 0, "A message is still being processed.");

        var userMsg = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "user",
            Content = request.Content,
            Status = "completed",
            CreatedAt = DateTime.UtcNow,
        };

        var assistantMsg = new ChatMessage
        {
            ChatSessionId = session.Id,
            Role = "assistant",
            Content = "",
            Status = "processing",
            CreatedAt = DateTime.UtcNow,
        };

        _db.ChatMessages.Add(userMsg);
        _db.ChatMessages.Add(assistantMsg);
        session.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _chatService.Enqueue(assistantMsg.Id, request.ModelOverride);

        return new SendChatMessageResult(true, userMsg.Id, assistantMsg.Id);
    }
}

// ── DeleteChatSession ────────────────────────────────────

public record DeleteChatSessionCommand(int SessionId) : IRequest<bool>;

public class DeleteChatSessionHandler : IRequestHandler<DeleteChatSessionCommand, bool>
{
    private readonly GlucoseDbContext _db;

    public DeleteChatSessionHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteChatSessionCommand request, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);

        if (session == null) return false;

        _db.ChatMessages.RemoveRange(session.Messages);
        _db.ChatSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── DeleteAllChatSessions ────────────────────────────────

public record DeleteAllChatSessionsCommand : IRequest<int>;

public class DeleteAllChatSessionsHandler : IRequestHandler<DeleteAllChatSessionsCommand, int>
{
    private readonly GlucoseDbContext _db;

    public DeleteAllChatSessionsHandler(GlucoseDbContext db) => _db = db;

    public async Task<int> Handle(DeleteAllChatSessionsCommand request, CancellationToken ct)
    {
        var sessions = await _db.ChatSessions
            .Include(s => s.Messages)
            .ToListAsync(ct);

        if (sessions.Count == 0) return 0;

        foreach (var s in sessions)
            _db.ChatMessages.RemoveRange(s.Messages);
        _db.ChatSessions.RemoveRange(sessions);
        await _db.SaveChangesAsync(ct);
        return sessions.Count;
    }
}

// ── CreateChatTemplate ───────────────────────────────────

public record CreateChatTemplateCommand(
    string Name, string Category, string SystemPrompt, string UserPromptTemplate
) : IRequest<ChatPromptTemplateDto>;

public class CreateChatTemplateHandler : IRequestHandler<CreateChatTemplateCommand, ChatPromptTemplateDto>
{
    private readonly GlucoseDbContext _db;

    public CreateChatTemplateHandler(GlucoseDbContext db) => _db = db;

    public async Task<ChatPromptTemplateDto> Handle(CreateChatTemplateCommand request, CancellationToken ct)
    {
        var template = new ChatPromptTemplate
        {
            Name = request.Name,
            Category = request.Category,
            SystemPrompt = request.SystemPrompt,
            UserPromptTemplate = request.UserPromptTemplate,
            IsBuiltIn = false,
            SortOrder = 100,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.ChatPromptTemplates.Add(template);
        await _db.SaveChangesAsync(ct);

        return MapTemplate(template);
    }

    private static ChatPromptTemplateDto MapTemplate(ChatPromptTemplate t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Category = t.Category,
        SystemPrompt = t.SystemPrompt,
        UserPromptTemplate = t.UserPromptTemplate,
        IsBuiltIn = t.IsBuiltIn,
        SortOrder = t.SortOrder,
    };
}

// ── UpdateChatTemplate ───────────────────────────────────

public record UpdateChatTemplateCommand(
    int Id, string Name, string Category, string SystemPrompt, string UserPromptTemplate
) : IRequest<bool>;

public class UpdateChatTemplateHandler : IRequestHandler<UpdateChatTemplateCommand, bool>
{
    private readonly GlucoseDbContext _db;

    public UpdateChatTemplateHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(UpdateChatTemplateCommand request, CancellationToken ct)
    {
        var t = await _db.ChatPromptTemplates.FindAsync(new object[] { request.Id }, ct);
        if (t == null || t.IsBuiltIn) return false;

        t.Name = request.Name;
        t.Category = request.Category;
        t.SystemPrompt = request.SystemPrompt;
        t.UserPromptTemplate = request.UserPromptTemplate;
        t.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return true;
    }
}

// ── DeleteChatTemplate ───────────────────────────────────

public record DeleteChatTemplateCommand(int Id) : IRequest<bool>;

public class DeleteChatTemplateHandler : IRequestHandler<DeleteChatTemplateCommand, bool>
{
    private readonly GlucoseDbContext _db;

    public DeleteChatTemplateHandler(GlucoseDbContext db) => _db = db;

    public async Task<bool> Handle(DeleteChatTemplateCommand request, CancellationToken ct)
    {
        var t = await _db.ChatPromptTemplates.FindAsync(new object[] { request.Id }, ct);
        if (t == null || t.IsBuiltIn) return false;

        _db.ChatPromptTemplates.Remove(t);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
