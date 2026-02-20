namespace GlucoseAPI.Application.Interfaces;

/// <summary>
/// Central abstraction for application-level event logging.
/// All services use this single interface to record significant operations,
/// warnings, and errors into the database and push them to the UI in real time.
/// </summary>
public interface IEventLogger
{
    // ── Convenience methods ─────────────────────────────────

    Task LogInfoAsync(string category, string message, string? source = null,
        string? detail = null, int? relatedEntityId = null, string? relatedEntityType = null,
        int? numericValue = null, int? durationMs = null);

    Task LogWarningAsync(string category, string message, string? source = null,
        string? detail = null, int? relatedEntityId = null, string? relatedEntityType = null,
        int? numericValue = null, int? durationMs = null);

    Task LogErrorAsync(string category, string message, string? source = null,
        string? detail = null, int? relatedEntityId = null, string? relatedEntityType = null,
        int? numericValue = null, int? durationMs = null);
}

/// <summary>
/// Well-known category constants to keep event log categories consistent.
/// </summary>
public static class EventCategory
{
    public const string Glucose = "glucose";
    public const string Notes = "notes";
    public const string Events = "events";
    public const string Analysis = "analysis";
    public const string Daily = "daily";
    public const string Comparison = "comparison";
    public const string Summary = "summary";
    public const string Backup = "backup";
    public const string Settings = "settings";
    public const string System = "system";
    public const string Sync = "sync";
    public const string Chat = "chat";
}
