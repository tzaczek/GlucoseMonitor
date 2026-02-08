using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

[Table("AppSettings")]
public class AppSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Setting key name.</summary>
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Setting value (stored as string).</summary>
    [MaxLength(500)]
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Well-known setting keys.</summary>
public static class SettingKeys
{
    public const string LibreEmail = "LibreLink:Email";
    public const string LibrePassword = "LibreLink:Password";
    public const string LibrePatientId = "LibreLink:PatientId";
    public const string LibreRegion = "LibreLink:Region";
    public const string LibreVersion = "LibreLink:Version";
    public const string FetchIntervalMinutes = "LibreLink:FetchIntervalMinutes";

    // Analysis / GPT settings
    public const string GptApiKey = "Analysis:GptApiKey";
    public const string AnalysisFolderName = "Analysis:NotesFolderName";
    public const string AnalysisIntervalMinutes = "Analysis:IntervalMinutes";

    // Display / timezone
    public const string DisplayTimeZone = "Display:TimeZone";

    // Re-analysis throttle
    public const string ReanalysisMinIntervalMinutes = "Analysis:ReanalysisMinIntervalMinutes";
}

/// <summary>DTO for the settings page.</summary>
public class LibreSettingsDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Region { get; set; } = "eu";
    public string Version { get; set; } = "4.16.0";
    public int FetchIntervalMinutes { get; set; } = 5;
    public bool IsConfigured { get; set; }
}

/// <summary>DTO for connection test result.</summary>
public class ConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<PatientInfo>? Patients { get; set; }
}

public class PatientInfo
{
    public string? PatientId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

/// <summary>DTO for analysis / GPT settings.</summary>
public class AnalysisSettingsDto
{
    public string GptApiKey { get; set; } = string.Empty;
    public string NotesFolderName { get; set; } = "Cukier";
    public int AnalysisIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Minimum interval (in minutes) between re-analyses triggered by new glucose data.
    /// New events always bypass this cooldown. Default: 30 minutes.
    /// </summary>
    public int ReanalysisMinIntervalMinutes { get; set; } = 30;

    public string TimeZone { get; set; } = "Europe/Warsaw";
    public bool IsConfigured { get; set; }
}

/// <summary>Combined settings DTO returned to the UI (LibreLink + Analysis).</summary>
public class AllSettingsDto
{
    public LibreSettingsDto Libre { get; set; } = new();
    public AnalysisSettingsDto Analysis { get; set; } = new();
}
