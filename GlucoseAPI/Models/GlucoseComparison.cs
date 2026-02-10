using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

/// <summary>
/// Stores a comparison between two time periods of glucose data.
/// Includes statistics for each period and an AI-generated analysis
/// of what differed and its impact.
/// </summary>
[Table("GlucoseComparisons")]
public class GlucoseComparison
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Optional user label (e.g., "Weekday vs Weekend").</summary>
    [MaxLength(500)]
    public string? Name { get; set; }

    // ── Period A boundaries ────────────────────────────────
    public DateTime PeriodAStart { get; set; }
    public DateTime PeriodAEnd { get; set; }
    [MaxLength(500)]
    public string? PeriodALabel { get; set; }

    // ── Period B boundaries ────────────────────────────────
    public DateTime PeriodBStart { get; set; }
    public DateTime PeriodBEnd { get; set; }
    [MaxLength(500)]
    public string? PeriodBLabel { get; set; }

    [MaxLength(100)]
    public string TimeZone { get; set; } = string.Empty;

    // ── Period A glucose statistics ───────────────────────
    public int PeriodAReadingCount { get; set; }
    public double? PeriodAGlucoseMin { get; set; }
    public double? PeriodAGlucoseMax { get; set; }
    public double? PeriodAGlucoseAvg { get; set; }
    public double? PeriodAGlucoseStdDev { get; set; }
    public double? PeriodATimeInRange { get; set; }
    public double? PeriodATimeAboveRange { get; set; }
    public double? PeriodATimeBelowRange { get; set; }
    public int PeriodAEventCount { get; set; }
    public string? PeriodAEventTitles { get; set; }

    // ── Period B glucose statistics ───────────────────────
    public int PeriodBReadingCount { get; set; }
    public double? PeriodBGlucoseMin { get; set; }
    public double? PeriodBGlucoseMax { get; set; }
    public double? PeriodBGlucoseAvg { get; set; }
    public double? PeriodBGlucoseStdDev { get; set; }
    public double? PeriodBTimeInRange { get; set; }
    public double? PeriodBTimeAboveRange { get; set; }
    public double? PeriodBTimeBelowRange { get; set; }
    public int PeriodBEventCount { get; set; }
    public string? PeriodBEventTitles { get; set; }

    // ── AI analysis ──────────────────────────────────────
    public string? AiAnalysis { get; set; }
    [MaxLength(10)]
    public string? AiClassification { get; set; }

    // ── Processing state ─────────────────────────────────
    /// <summary>pending | processing | completed | failed</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// ── DTOs ────────────────────────────────────────────────

/// <summary>Summary DTO for the comparisons list.</summary>
public class ComparisonSummaryDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public DateTime PeriodAStart { get; set; }
    public DateTime PeriodAEnd { get; set; }
    public string? PeriodALabel { get; set; }
    public DateTime PeriodBStart { get; set; }
    public DateTime PeriodBEnd { get; set; }
    public string? PeriodBLabel { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AiClassification { get; set; }
    public double? PeriodAGlucoseAvg { get; set; }
    public double? PeriodBGlucoseAvg { get; set; }
    public double? PeriodATimeInRange { get; set; }
    public double? PeriodBTimeInRange { get; set; }
    public int PeriodAEventCount { get; set; }
    public int PeriodBEventCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Full detail DTO with glucose readings for chart overlay.</summary>
public class ComparisonDetailDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    // Period A
    public DateTime PeriodAStart { get; set; }
    public DateTime PeriodAEnd { get; set; }
    public string? PeriodALabel { get; set; }
    public ComparisonPeriodStatsDto PeriodAStats { get; set; } = new();
    public List<ComparisonReadingDto> PeriodAReadings { get; set; } = new();
    public List<ComparisonEventDto> PeriodAEvents { get; set; } = new();

    // Period B
    public DateTime PeriodBStart { get; set; }
    public DateTime PeriodBEnd { get; set; }
    public string? PeriodBLabel { get; set; }
    public ComparisonPeriodStatsDto PeriodBStats { get; set; } = new();
    public List<ComparisonReadingDto> PeriodBReadings { get; set; } = new();
    public List<ComparisonEventDto> PeriodBEvents { get; set; } = new();

    // AI
    public string? AiAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ComparisonPeriodStatsDto
{
    public int ReadingCount { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseStdDev { get; set; }
    public double? TimeInRange { get; set; }
    public double? TimeAboveRange { get; set; }
    public double? TimeBelowRange { get; set; }
    public int EventCount { get; set; }
    public string? EventTitles { get; set; }
}

/// <summary>Glucose reading with offset from period start (for chart normalization).</summary>
public class ComparisonReadingDto
{
    public DateTime Timestamp { get; set; }
    public double Value { get; set; }
    /// <summary>Hours since the start of this period.</summary>
    public double OffsetHours { get; set; }
}

/// <summary>Event summary within a comparison period.</summary>
public class ComparisonEventDto
{
    public int Id { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteContent { get; set; }
    public DateTime EventTimestamp { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public string? AiClassification { get; set; }
    /// <summary>Hours since the start of this period.</summary>
    public double OffsetHours { get; set; }
}
