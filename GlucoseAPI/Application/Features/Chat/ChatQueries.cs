using GlucoseAPI.Application.Common;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Chat;

// ── ListChatSessions ─────────────────────────────────────

public record ListChatSessionsQuery(int? Limit = null, int Offset = 0) : IRequest<PagedResult<ChatSessionListDto>>;

public class ListChatSessionsHandler : IRequestHandler<ListChatSessionsQuery, PagedResult<ChatSessionListDto>>
{
    private readonly GlucoseDbContext _db;

    public ListChatSessionsHandler(GlucoseDbContext db) => _db = db;

    public async Task<PagedResult<ChatSessionListDto>> Handle(ListChatSessionsQuery request, CancellationToken ct)
    {
        var baseQuery = _db.ChatSessions
            .OrderByDescending(s => s.UpdatedAt);

        var totalCount = await baseQuery.CountAsync(ct);

        IQueryable<ChatSession> query = baseQuery.Skip(request.Offset);
        if (request.Limit.HasValue)
            query = query.Take(request.Limit.Value);

        var sessions = await query
            .Select(s => new
            {
                s.Id, s.Title, s.PeriodStart, s.PeriodEnd, s.PeriodDescription,
                s.TemplateName, s.Status, s.PeriodsJson,
                MessageCount = s.Messages.Count,
                s.CreatedAt, s.UpdatedAt,
            })
            .ToListAsync(ct);

        var items = sessions.Select(s => new ChatSessionListDto
        {
            Id = s.Id, Title = s.Title, PeriodStart = s.PeriodStart,
            PeriodEnd = s.PeriodEnd, PeriodDescription = s.PeriodDescription,
            TemplateName = s.TemplateName, Status = s.Status,
            MessageCount = s.MessageCount,
            CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt,
            Periods = DeserializePeriods(s.PeriodsJson),
        }).ToList();
        return new PagedResult<ChatSessionListDto>(items, totalCount);
    }

    private static List<ChatPeriodDto> DeserializePeriods(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var periods = System.Text.Json.JsonSerializer.Deserialize<List<ChatPeriod>>(json);
            return periods?.Select(p => new ChatPeriodDto
            {
                Name = p.Name, Start = p.Start, End = p.End, Color = p.Color,
            }).ToList() ?? new();
        }
        catch { return new(); }
    }
}

// ── GetChatSession ───────────────────────────────────────

public record GetChatSessionQuery(int SessionId) : IRequest<ChatSessionDetailDto?>;

public class GetChatSessionHandler : IRequestHandler<GetChatSessionQuery, ChatSessionDetailDto?>
{
    private readonly GlucoseDbContext _db;

    public GetChatSessionHandler(GlucoseDbContext db) => _db = db;

    public async Task<ChatSessionDetailDto?> Handle(GetChatSessionQuery request, CancellationToken ct)
    {
        var session = await _db.ChatSessions
            .Include(s => s.Messages.OrderBy(m => m.CreatedAt))
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, ct);

        if (session == null) return null;

        return new ChatSessionDetailDto
        {
            Id = session.Id,
            Title = session.Title,
            PeriodStart = session.PeriodStart,
            PeriodEnd = session.PeriodEnd,
            PeriodDescription = session.PeriodDescription,
            TemplateName = session.TemplateName,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            UpdatedAt = session.UpdatedAt,
            Periods = session.Periods.Select(p => new ChatPeriodDto
            {
                Name = p.Name, Start = p.Start, End = p.End, Color = p.Color,
            }).ToList(),
            Messages = session.Messages.Select(m => new ChatMessageDto
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                AiModel = m.AiModel,
                InputTokens = m.InputTokens,
                OutputTokens = m.OutputTokens,
                CostUsd = m.CostUsd,
                DurationMs = m.DurationMs,
                ReferencedEventIds = m.ReferencedEventIds,
                Status = m.Status,
                ErrorMessage = m.ErrorMessage,
                CreatedAt = m.CreatedAt,
            }).ToList(),
        };
    }
}

// ── ListChatTemplates ────────────────────────────────────

public record ListChatTemplatesQuery : IRequest<List<ChatPromptTemplateDto>>;

public class ListChatTemplatesHandler : IRequestHandler<ListChatTemplatesQuery, List<ChatPromptTemplateDto>>
{
    private readonly GlucoseDbContext _db;

    public ListChatTemplatesHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<ChatPromptTemplateDto>> Handle(ListChatTemplatesQuery request, CancellationToken ct)
    {
        return await _db.ChatPromptTemplates
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(t => new ChatPromptTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Category = t.Category,
                SystemPrompt = t.SystemPrompt,
                UserPromptTemplate = t.UserPromptTemplate,
                IsBuiltIn = t.IsBuiltIn,
                SortOrder = t.SortOrder,
            })
            .ToListAsync(ct);
    }
}
