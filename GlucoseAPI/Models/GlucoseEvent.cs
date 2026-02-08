using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

/// <summary>
/// Represents a correlated event linking a Samsung Note (e.g., a meal from the "Cukier" folder)
/// with glucose readings before and after the event, plus AI analysis.
/// </summary>
[Table("GlucoseEvents")]
public class GlucoseEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Foreign key to the Samsung Note that triggered this event.</summary>
    public int SamsungNoteId { get; set; }

    /// <summary>UUID of the Samsung Note (for quick lookup).</summary>
    [MaxLength(200)]
    public string NoteUuid { get; set; } = string.Empty;

    /// <summary>Snapshot of the note title at processing time.</summary>
    [MaxLength(500)]
    public string NoteTitle { get; set; } = string.Empty;

    /// <summary>Snapshot of the note text content at processing time.</summary>
    public string? NoteContent { get; set; }

    /// <summary>When the note was created/modified — the "event" time (UTC).</summary>
    public DateTime EventTimestamp { get; set; }

    /// <summary>Start of the glucose monitoring period — previous event time or default lookback (UTC).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>End of the glucose monitoring period — next event time or default lookahead (UTC).</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>Number of glucose readings in this period.</summary>
    public int ReadingCount { get; set; }

    /// <summary>Glucose value at or near the event time (mg/dL).</summary>
    public double? GlucoseAtEvent { get; set; }

    /// <summary>Minimum glucose in the period (mg/dL).</summary>
    public double? GlucoseMin { get; set; }

    /// <summary>Maximum glucose in the period (mg/dL).</summary>
    public double? GlucoseMax { get; set; }

    /// <summary>Average glucose in the period (mg/dL).</summary>
    public double? GlucoseAvg { get; set; }

    /// <summary>Maximum glucose spike after the event (mg/dL above baseline).</summary>
    public double? GlucoseSpike { get; set; }

    /// <summary>Time of peak glucose after the event (UTC).</summary>
    public DateTime? PeakTime { get; set; }

    /// <summary>GPT AI analysis of the glucose response.</summary>
    public string? AiAnalysis { get; set; }

    /// <summary>AI classification of the glucose response: "green" (ok), "yellow" (concerning), "red" (bad).</summary>
    [MaxLength(10)]
    public string? AiClassification { get; set; }

    /// <summary>Whether this event has been fully processed (glucose data gathered + AI analyzed).</summary>
    public bool IsProcessed { get; set; }

    /// <summary>When this event was processed (UTC).</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>When this record was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this record was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Analysis History ─────────────────────────────────────────

/// <summary>
/// Stores a snapshot of each AI analysis performed on a GlucoseEvent,
/// including glucose statistics at the time of analysis.
/// Multiple rows per event — old analyses are never deleted.
/// </summary>
[Table("EventAnalysisHistory")]
public class EventAnalysisHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>FK to the GlucoseEvent.</summary>
    public int GlucoseEventId { get; set; }

    /// <summary>The AI-generated analysis text.</summary>
    public string? AiAnalysis { get; set; }

    /// <summary>AI classification: "green", "yellow", or "red".</summary>
    [MaxLength(10)]
    public string? AiClassification { get; set; }

    /// <summary>When this analysis was performed (UTC).</summary>
    public DateTime AnalyzedAt { get; set; }

    /// <summary>PeriodStart used when this analysis was generated (UTC).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>PeriodEnd used when this analysis was generated (UTC).</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>Number of glucose readings in the period at analysis time.</summary>
    public int ReadingCount { get; set; }

    /// <summary>Human-readable reason for the analysis run.</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    // ── Glucose statistics snapshot at analysis time ──────────

    /// <summary>Glucose value at or near the event time (mg/dL).</summary>
    public double? GlucoseAtEvent { get; set; }

    /// <summary>Minimum glucose in the period (mg/dL).</summary>
    public double? GlucoseMin { get; set; }

    /// <summary>Maximum glucose in the period (mg/dL).</summary>
    public double? GlucoseMax { get; set; }

    /// <summary>Average glucose in the period (mg/dL).</summary>
    public double? GlucoseAvg { get; set; }

    /// <summary>Maximum glucose spike after the event (mg/dL above baseline).</summary>
    public double? GlucoseSpike { get; set; }

    /// <summary>Time of peak glucose after the event (UTC).</summary>
    public DateTime? PeakTime { get; set; }
}

// ── AI Usage Log ─────────────────────────────────────────────

/// <summary>
/// Tracks token usage and cost for each GPT API call.
/// One row per API call.
/// </summary>
[Table("AiUsageLogs")]
public class AiUsageLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>FK to the GlucoseEvent that triggered this call (nullable for future non-event calls).</summary>
    public int? GlucoseEventId { get; set; }

    /// <summary>The GPT model used (e.g. "gpt-5-mini").</summary>
    [MaxLength(100)]
    public string Model { get; set; } = string.Empty;

    /// <summary>Number of input (prompt) tokens.</summary>
    public int InputTokens { get; set; }

    /// <summary>Number of output (completion) tokens.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Total tokens (input + output).</summary>
    public int TotalTokens { get; set; }

    /// <summary>The reason the API call was made.</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>Whether the API call was successful.</summary>
    public bool Success { get; set; }

    /// <summary>HTTP status code from the API.</summary>
    public int? HttpStatusCode { get; set; }

    /// <summary>Finish reason from the API (e.g. "stop", "length").</summary>
    [MaxLength(50)]
    public string? FinishReason { get; set; }

    /// <summary>When this API call was made (UTC).</summary>
    public DateTime CalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>Duration of the API call in milliseconds.</summary>
    public int? DurationMs { get; set; }
}

// ── DTOs ────────────────────────────────────────────────────

/// <summary>Summary DTO for the events list.</summary>
public class GlucoseEventSummaryDto
{
    public int Id { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteContentPreview { get; set; }
    public DateTime EventTimestamp { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseSpike { get; set; }
    public bool IsProcessed { get; set; }
    public bool HasAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public int AnalysisCount { get; set; }
}

/// <summary>Full detail DTO including glucose readings and AI analysis.</summary>
public class GlucoseEventDetailDto
{
    public int Id { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteContent { get; set; }
    public DateTime EventTimestamp { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseSpike { get; set; }
    public DateTime? PeakTime { get; set; }
    public string? AiAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public List<GlucoseReadingDto> Readings { get; set; } = new();
    public List<EventAnalysisHistoryDto> AnalysisHistory { get; set; } = new();
}

/// <summary>DTO for a single analysis history entry (includes glucose stats snapshot).</summary>
public class EventAnalysisHistoryDto
{
    public int Id { get; set; }
    public string? AiAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int ReadingCount { get; set; }
    public string? Reason { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseSpike { get; set; }
    public DateTime? PeakTime { get; set; }
}

