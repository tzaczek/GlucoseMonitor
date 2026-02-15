using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

/// <summary>
/// Stores a historical snapshot of a daily summary generation.
/// Each time a daily summary is generated (auto or manual), a snapshot is saved
/// with all the data that was used and the resulting AI analysis.
/// </summary>
[Table("DailySummarySnapshots")]
public class DailySummarySnapshot
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>FK to the parent DailySummary.</summary>
    public int DailySummaryId { get; set; }

    /// <summary>The calendar date this snapshot covers.</summary>
    public DateTime Date { get; set; }

    /// <summary>When this snapshot was generated (UTC).</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>How this generation was triggered: "auto" or "manual".</summary>
    [MaxLength(20)]
    public string Trigger { get; set; } = "auto";

    // ── Data coverage ────────────────────────────────────────

    /// <summary>Start of the period in UTC that data was pulled from.</summary>
    public DateTime DataStartUtc { get; set; }

    /// <summary>End of the period in UTC that data was pulled from.</summary>
    public DateTime DataEndUtc { get; set; }

    /// <summary>Timestamp of the earliest glucose reading actually used.</summary>
    public DateTime? FirstReadingUtc { get; set; }

    /// <summary>Timestamp of the latest glucose reading actually used.</summary>
    public DateTime? LastReadingUtc { get; set; }

    /// <summary>The timezone used when this snapshot was generated.</summary>
    [MaxLength(100)]
    public string TimeZone { get; set; } = string.Empty;

    // ── Event aggregation ────────────────────────────────────

    /// <summary>Number of events included in this snapshot.</summary>
    public int EventCount { get; set; }

    /// <summary>Comma-separated list of event IDs included.</summary>
    public string? EventIds { get; set; }

    /// <summary>Brief listing of event titles.</summary>
    public string? EventTitles { get; set; }

    // ── Glucose statistics ───────────────────────────────────

    /// <summary>Total number of glucose readings used.</summary>
    public int ReadingCount { get; set; }

    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseStdDev { get; set; }
    public double? TimeInRange { get; set; }
    public double? TimeAboveRange { get; set; }
    public double? TimeBelowRange { get; set; }

    // ── AI Analysis ──────────────────────────────────────────

    /// <summary>The AI analysis text generated for this snapshot.</summary>
    public string? AiAnalysis { get; set; }

    /// <summary>AI classification: "green", "yellow", or "red".</summary>
    [MaxLength(10)]
    public string? AiClassification { get; set; }

    /// <summary>GPT model used for this analysis (e.g. "gpt-4o-mini").</summary>
    [MaxLength(50)]
    public string? AiModel { get; set; }

    /// <summary>Whether AI analysis completed successfully.</summary>
    public bool IsProcessed { get; set; }
}

// ── DTOs ────────────────────────────────────────────────────

/// <summary>List DTO for snapshot history.</summary>
public class DailySummarySnapshotDto
{
    public int Id { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public int ReadingCount { get; set; }
    public int EventCount { get; set; }
    public DateTime? FirstReadingUtc { get; set; }
    public DateTime? LastReadingUtc { get; set; }
    public double? GlucoseAvg { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? TimeInRange { get; set; }
    public bool IsProcessed { get; set; }
    public bool HasAnalysis { get; set; }
    public string? AiClassification { get; set; }
}

/// <summary>Full detail DTO for a single snapshot.</summary>
public class DailySummarySnapshotDetailDto : DailySummarySnapshotDto
{
    public DateTime Date { get; set; }
    public DateTime DataStartUtc { get; set; }
    public DateTime DataEndUtc { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public string? EventIds { get; set; }
    public string? EventTitles { get; set; }
    public double? GlucoseStdDev { get; set; }
    public double? TimeAboveRange { get; set; }
    public double? TimeBelowRange { get; set; }
    public string? AiAnalysis { get; set; }
    public string? AiModel { get; set; }
}
