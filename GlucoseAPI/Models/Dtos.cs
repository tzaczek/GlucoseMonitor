namespace GlucoseAPI.Models;

/// <summary>DTO returned to the React frontend.</summary>
public class GlucoseReadingDto
{
    public int Id { get; set; }
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
    public int TrendArrow { get; set; }
    public bool IsHigh { get; set; }
    public bool IsLow { get; set; }
    public string? PatientId { get; set; }
}

public class GlucoseStatsDto
{
    public double Average { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public int TotalReadings { get; set; }
    public double TimeInRange { get; set; } // percentage 70-180 mg/dL
    public GlucoseReadingDto? LatestReading { get; set; }
}
