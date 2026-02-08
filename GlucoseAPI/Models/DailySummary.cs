using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

/// <summary>
/// Stores a daily summary that aggregates all events and glucose readings for a single day,
/// along with an AI-generated analysis of the full day.
/// One row per calendar day (in the user's configured timezone).
/// </summary>
[Table("DailySummaries")]
public class DailySummary
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>The calendar date this summary covers (date only, no time component).</summary>
    public DateTime Date { get; set; }

    /// <summary>Start of the day in UTC (midnight local → UTC).</summary>
    public DateTime PeriodStartUtc { get; set; }

    /// <summary>End of the day in UTC (next midnight local → UTC).</summary>
    public DateTime PeriodEndUtc { get; set; }

    /// <summary>The timezone used when this summary was generated.</summary>
    [MaxLength(100)]
    public string TimeZone { get; set; } = string.Empty;

    // ── Event aggregation ────────────────────────────────────

    /// <summary>Number of glucose events (meals/activities) that occurred this day.</summary>
    public int EventCount { get; set; }

    /// <summary>Comma-separated list of event IDs included in this summary.</summary>
    public string? EventIds { get; set; }

    /// <summary>Brief listing of all event titles for the day.</summary>
    public string? EventTitles { get; set; }

    // ── Glucose statistics for the whole day ─────────────────

    /// <summary>Total number of glucose readings for the day.</summary>
    public int ReadingCount { get; set; }

    /// <summary>Minimum glucose value for the day (mg/dL).</summary>
    public double? GlucoseMin { get; set; }

    /// <summary>Maximum glucose value for the day (mg/dL).</summary>
    public double? GlucoseMax { get; set; }

    /// <summary>Average glucose value for the day (mg/dL).</summary>
    public double? GlucoseAvg { get; set; }

    /// <summary>Standard deviation of glucose values for the day.</summary>
    public double? GlucoseStdDev { get; set; }

    /// <summary>Percentage of time glucose was in range (70-180 mg/dL).</summary>
    public double? TimeInRange { get; set; }

    /// <summary>Percentage of time glucose was above range (&gt;180 mg/dL).</summary>
    public double? TimeAboveRange { get; set; }

    /// <summary>Percentage of time glucose was below range (&lt;70 mg/dL).</summary>
    public double? TimeBelowRange { get; set; }

    // ── AI analysis ──────────────────────────────────────────

    /// <summary>GPT AI daily summary analysis.</summary>
    public string? AiAnalysis { get; set; }

    /// <summary>AI classification of the day: "green" (ok), "yellow" (concerning), "red" (bad).</summary>
    [MaxLength(10)]
    public string? AiClassification { get; set; }

    /// <summary>Whether this summary has been fully processed.</summary>
    public bool IsProcessed { get; set; }

    /// <summary>When this summary was last processed (UTC).</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>When this record was created (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this record was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── DTOs ────────────────────────────────────────────────────

/// <summary>Summary DTO for the daily summaries list.</summary>
public class DailySummaryListDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public int EventCount { get; set; }
    public string? EventTitles { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? TimeInRange { get; set; }
    public bool IsProcessed { get; set; }
    public bool HasAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public int SnapshotCount { get; set; }
}

/// <summary>Full detail DTO for a daily summary.</summary>
public class DailySummaryDetailDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public string? EventIds { get; set; }
    public string? EventTitles { get; set; }
    public int ReadingCount { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseStdDev { get; set; }
    public double? TimeInRange { get; set; }
    public double? TimeAboveRange { get; set; }
    public double? TimeBelowRange { get; set; }
    public string? AiAnalysis { get; set; }
    public string? AiClassification { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int SnapshotCount { get; set; }
    public List<DailySummarySnapshotDto> Snapshots { get; set; } = new();
    public List<GlucoseEventSummaryDto> Events { get; set; } = new();
    public List<GlucoseReadingDto> Readings { get; set; } = new();
}
