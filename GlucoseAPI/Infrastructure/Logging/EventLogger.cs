using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GlucoseAPI.Infrastructure.Logging;

/// <summary>
/// Singleton implementation of <see cref="IEventLogger"/>.
/// Writes every event to the EventLogs table and pushes a real-time notification via SignalR.
/// Uses IServiceProvider to create scoped DbContexts since it outlives any single request.
/// </summary>
public class EventLogger : IEventLogger
{
    private readonly IServiceProvider _serviceProvider;
    private readonly INotificationService _notifications;
    private readonly ILogger<EventLogger> _logger;

    public EventLogger(
        IServiceProvider serviceProvider,
        INotificationService notifications,
        ILogger<EventLogger> logger)
    {
        _serviceProvider = serviceProvider;
        _notifications = notifications;
        _logger = logger;
    }

    public Task LogInfoAsync(string category, string message, string? source = null,
        string? detail = null, int? relatedEntityId = null, string? relatedEntityType = null,
        int? numericValue = null, int? durationMs = null)
        => WriteAsync("info", category, message, source, detail, relatedEntityId, relatedEntityType, numericValue, durationMs);

    public Task LogWarningAsync(string category, string message, string? source = null,
        string? detail = null, int? relatedEntityId = null, string? relatedEntityType = null,
        int? numericValue = null, int? durationMs = null)
        => WriteAsync("warning", category, message, source, detail, relatedEntityId, relatedEntityType, numericValue, durationMs);

    public Task LogErrorAsync(string category, string message, string? source = null,
        string? detail = null, int? relatedEntityId = null, string? relatedEntityType = null,
        int? numericValue = null, int? durationMs = null)
        => WriteAsync("error", category, message, source, detail, relatedEntityId, relatedEntityType, numericValue, durationMs);

    private async Task WriteAsync(string level, string category, string message,
        string? source, string? detail, int? relatedEntityId, string? relatedEntityType,
        int? numericValue, int? durationMs)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

            var entry = new EventLog
            {
                Timestamp = DateTime.UtcNow,
                Level = level,
                Category = category,
                Message = message.Length > 500 ? message[..500] : message,
                Detail = detail != null && detail.Length > 4000 ? detail[..4000] : detail,
                Source = source,
                RelatedEntityId = relatedEntityId,
                RelatedEntityType = relatedEntityType,
                NumericValue = numericValue,
                DurationMs = durationMs,
            };

            db.EventLogs.Add(entry);
            await db.SaveChangesAsync();

            // Fire-and-forget notification to UI
            _ = _notifications.NotifyEventLogsUpdatedAsync(1);
        }
        catch (Exception ex)
        {
            // Never let event logging break the caller — fall back to standard logging
            _logger.LogWarning(ex, "Failed to write event log: [{Level}] {Category} — {Message}", level, category, message);
        }
    }
}
