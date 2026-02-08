using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

[Table("GlucoseReadings")]
public class GlucoseReading
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Glucose value in mg/dL.</summary>
    public double Value { get; set; }

    /// <summary>Timestamp of the reading (UTC).</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Trend arrow direction (1-5). 3 = flat, 1 = falling fast, 5 = rising fast.</summary>
    public int TrendArrow { get; set; }

    /// <summary>Factory timestamp string from the sensor.</summary>
    public string? FactoryTimestamp { get; set; }

    /// <summary>Whether the reading is marked as high.</summary>
    public bool IsHigh { get; set; }

    /// <summary>Whether the reading is marked as low.</summary>
    public bool IsLow { get; set; }

    /// <summary>Patient ID this reading belongs to.</summary>
    public string? PatientId { get; set; }

    /// <summary>When this record was inserted into the database (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
