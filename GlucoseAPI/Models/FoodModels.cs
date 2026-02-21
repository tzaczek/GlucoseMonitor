using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

[Table("FoodItems")]
public class FoodItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? NameEn { get; set; }

    [MaxLength(200)]
    public string NormalizedName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Category { get; set; }

    public int OccurrenceCount { get; set; }

    public double? AvgSpike { get; set; }
    public double? AvgGlucoseAtEvent { get; set; }
    public double? AvgGlucoseMax { get; set; }
    public double? AvgGlucoseMin { get; set; }
    public double? AvgRecoveryMinutes { get; set; }

    public double? WorstSpike { get; set; }
    public double? BestSpike { get; set; }

    public int GreenCount { get; set; }
    public int YellowCount { get; set; }
    public int RedCount { get; set; }

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[Table("FoodEventLinks")]
public class FoodEventLink
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int FoodItemId { get; set; }
    [ForeignKey(nameof(FoodItemId))]
    public FoodItem? FoodItem { get; set; }

    public int GlucoseEventId { get; set; }
    [ForeignKey(nameof(GlucoseEventId))]
    public GlucoseEvent? GlucoseEvent { get; set; }

    public double? Spike { get; set; }
    public double? GlucoseAtEvent { get; set; }

    [MaxLength(10)]
    public string? AiClassification { get; set; }

    public double? RecoveryMinutes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// DTOs

public class FoodItemSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Category { get; set; }
    public int OccurrenceCount { get; set; }
    public double? AvgSpike { get; set; }
    public double? WorstSpike { get; set; }
    public double? BestSpike { get; set; }
    public double? AvgRecoveryMinutes { get; set; }
    public int GreenCount { get; set; }
    public int YellowCount { get; set; }
    public int RedCount { get; set; }
    public DateTime LastSeen { get; set; }
}

public class FoodItemDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Category { get; set; }
    public int OccurrenceCount { get; set; }
    public double? AvgSpike { get; set; }
    public double? AvgGlucoseAtEvent { get; set; }
    public double? AvgGlucoseMax { get; set; }
    public double? AvgGlucoseMin { get; set; }
    public double? AvgRecoveryMinutes { get; set; }
    public double? WorstSpike { get; set; }
    public double? BestSpike { get; set; }
    public int GreenCount { get; set; }
    public int YellowCount { get; set; }
    public int RedCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public List<FoodEventDto> Events { get; set; } = new();
}

public class FoodEventDto
{
    public int EventId { get; set; }
    public string NoteTitle { get; set; } = string.Empty;
    public string? NoteTitleEn { get; set; }
    public string? NoteContent { get; set; }
    public string? NoteContentEn { get; set; }
    public DateTime EventTimestamp { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public double? Spike { get; set; }
    public double? GlucoseAtEvent { get; set; }
    public double? GlucoseMin { get; set; }
    public double? GlucoseMax { get; set; }
    public double? GlucoseAvg { get; set; }
    public string? AiClassification { get; set; }
    public string? AiAnalysis { get; set; }
    public double? RecoveryMinutes { get; set; }
    public int ReadingCount { get; set; }
}

public class FoodExtractionResult
{
    public List<string> Foods { get; set; } = new();
    public string? Category { get; set; }
}
