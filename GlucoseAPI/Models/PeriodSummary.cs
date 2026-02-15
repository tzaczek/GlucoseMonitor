using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

/// <summary>
/// Stores a summary for an arbitrary user-chosen time period.
/// Similar to DailySummary but not restricted to a single calendar day.
/// Includes glucose stats, events, and an AI-generated analysis.
/// </summary>
[Table("PeriodSummaries")]
public class PeriodSummary
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Optional user label (e.g., "Weekend trip", "Fasting experiment").</summary>
    [MaxLength(500)]
    public string? Name { get; set; }

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    [MaxLength(100)]
    public string TimeZone { get; set; } = string.Empty;

    // ── Glucose statistics ────────────────────────────────
    public int ReadingCount { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseStdDev { get; set; }
    public double? TimeInRange { get; set; }
    public double? TimeAboveRange { get; set; }
    public double? TimeBelowRange { get; set; }

    // ── Events ───────────────────────────────────────────
    public int EventCount { get; set; }
    public string? EventIds { get; set; }
    public string? EventTitles { get; set; }

    // ── AI analysis ──────────────────────────────────────
    public string? AiAnalysis { get; set; }
    [MaxLength(10)]
    public string? AiClassification { get; set; }
    /// <summary>GPT model used for this analysis (e.g. "gpt-4o-mini").</summary>
    [MaxLength(50)]
    public string? AiModel { get; set; }

    // ── Processing state ─────────────────────────────────
    /// <summary>pending | processing | completed | failed</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// ── DTOs ────────────────────────────────────────────────

public class PeriodSummaryListDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AiClassification { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? TimeInRange { get; set; }
    public int EventCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PeriodSummaryDetailDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    // Stats
    public int ReadingCount { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseStdDev { get; set; }
    public double? TimeInRange { get; set; }
    public double? TimeAboveRange { get; set; }
    public double? TimeBelowRange { get; set; }

    // Events & readings for chart
    public int EventCount { get; set; }
    public List<PeriodSummaryEventDto> Events { get; set; } = new();
    public List<PeriodSummaryReadingDto> Readings { get; set; } = new();

    // AI
    public string? AiAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public string? AiModel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class PeriodSummaryReadingDto
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
}

public class PeriodSummaryEventDto
{
    public int Id { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteContent { get; set; }
    public DateTime EventTimestamp { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseSpike { get; set; }
    public string? AiClassification { get; set; }
}
