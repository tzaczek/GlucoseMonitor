using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

/// <summary>
/// Stores application-level event logs for auditing, diagnostics, and user visibility.
/// Every significant operation (data fetch, sync, analysis, backup, etc.) is recorded here.
/// </summary>
[Table("EventLogs")]
public class EventLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Timestamp of the event (UTC).</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Severity level: info, warning, error.</summary>
    [MaxLength(10)]
    public string Level { get; set; } = "info";

    /// <summary>Category grouping: glucose, notes, events, analysis, daily, comparison, summary, backup, settings, system.</summary>
    [MaxLength(30)]
    public string Category { get; set; } = "system";

    /// <summary>Human-readable summary of what happened.</summary>
    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional detailed description or data (e.g., affected IDs, error stack).</summary>
    public string? Detail { get; set; }

    /// <summary>The service or component that produced this event.</summary>
    [MaxLength(100)]
    public string? Source { get; set; }

    /// <summary>Optional related entity ID (e.g., event ID, comparison ID).</summary>
    public int? RelatedEntityId { get; set; }

    /// <summary>Optional related entity type (e.g., "GlucoseEvent", "Comparison").</summary>
    [MaxLength(50)]
    public string? RelatedEntityType { get; set; }

    /// <summary>Optional numeric value associated with the event (e.g., count of readings inserted).</summary>
    public int? NumericValue { get; set; }

    /// <summary>Optional duration of the operation in milliseconds.</summary>
    public int? DurationMs { get; set; }
}

// ── DTOs ────────────────────────────────────────────────

public class EventLogDto
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? Source { get; set; }
    public int? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public int? NumericValue { get; set; }
    public int? DurationMs { get; set; }
}

public class EventLogStatsDto
{
    public int TotalCount { get; set; }
    public int InfoCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime? OldestEntry { get; set; }
    public DateTime? NewestEntry { get; set; }
}
