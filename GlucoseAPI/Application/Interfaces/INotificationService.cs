namespace GlucoseAPI.Application.Interfaces;

/// <summary>
/// Port interface for real-time client notifications.
/// Abstracts SignalR so services don't depend on hub context directly.
/// </summary>
public interface INotificationService
{
    /// <summary>Notify clients that new glucose data has arrived.</summary>
    Task NotifyNewGlucoseDataAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that events have been updated.</summary>
    Task NotifyEventsUpdatedAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that daily summaries have been updated.</summary>
    Task NotifyDailySummariesUpdatedAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that AI usage data has changed.</summary>
    Task NotifyAiUsageUpdatedAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that Samsung Notes have been updated.</summary>
    Task NotifyNotesUpdatedAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that a comparison has been updated (created, completed, or failed).</summary>
    Task NotifyComparisonsUpdatedAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that a period summary has been updated (created, completed, or failed).</summary>
    Task NotifyPeriodSummariesUpdatedAsync(int count, CancellationToken ct = default);

    /// <summary>Notify clients that new event log entries have been written.</summary>
    Task NotifyEventLogsUpdatedAsync(int count, CancellationToken ct = default);
}
