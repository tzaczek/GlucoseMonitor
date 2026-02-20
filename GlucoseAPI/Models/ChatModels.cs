using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace GlucoseAPI.Models;

// ── Period value object ─────────────────────────────────

public class ChatPeriod
{
    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Color { get; set; } = "#6366f1";
}

// ── Entities ────────────────────────────────────────────

[Table("ChatSessions")]
public class ChatSession
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }

    [MaxLength(100)]
    public string? TemplateName { get; set; }

    [MaxLength(500)]
    public string? PeriodDescription { get; set; }

    public string? PeriodsJson { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "active";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    [NotMapped]
    public List<ChatPeriod> Periods
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PeriodsJson)) return new List<ChatPeriod>();
            try { return JsonSerializer.Deserialize<List<ChatPeriod>>(PeriodsJson) ?? new(); }
            catch { return new List<ChatPeriod>(); }
        }
        set => PeriodsJson = JsonSerializer.Serialize(value);
    }
}

[Table("ChatMessages")]
public class ChatMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int ChatSessionId { get; set; }

    [MaxLength(20)]
    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? AiModel { get; set; }

    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? CostUsd { get; set; }
    public int? DurationMs { get; set; }

    [MaxLength(500)]
    public string? ReferencedEventIds { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "completed";

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ChatSessionId))]
    public ChatSession Session { get; set; } = null!;
}

[Table("ChatPromptTemplates")]
public class ChatPromptTemplate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Category { get; set; } = "custom";

    public string SystemPrompt { get; set; } = string.Empty;

    public string UserPromptTemplate { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── DTOs ────────────────────────────────────────────────

public class ChatPeriodDto
{
    public string Name { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Color { get; set; } = "#6366f1";
}

public class ChatSessionListDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public string? PeriodDescription { get; set; }
    public string? TemplateName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ChatPeriodDto> Periods { get; set; } = new();
}

public class ChatSessionDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime? PeriodStart { get; set; }
    public DateTime? PeriodEnd { get; set; }
    public string? PeriodDescription { get; set; }
    public string? TemplateName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ChatPeriodDto> Periods { get; set; } = new();
    public List<ChatMessageDto> Messages { get; set; } = new();
}

public class ChatMessageDto
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AiModel { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? CostUsd { get; set; }
    public int? DurationMs { get; set; }
    public string? ReferencedEventIds { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChatPromptTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPromptTemplate { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public int SortOrder { get; set; }
}
