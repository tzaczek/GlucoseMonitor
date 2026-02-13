using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace GlucoseAPI.Infrastructure.Notifications;

/// <summary>
/// Infrastructure implementation of <see cref="INotificationService"/> using SignalR.
/// Wraps IHubContext so that services don't depend on SignalR directly.
/// </summary>
public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<GlucoseHub> _hubContext;

    public SignalRNotificationService(IHubContext<GlucoseHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyNewGlucoseDataAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("NewGlucoseData", count, ct);

    public Task NotifyEventsUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("EventsUpdated", count, ct);

    public Task NotifyDailySummariesUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("DailySummariesUpdated", count, ct);

    public Task NotifyAiUsageUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("AiUsageUpdated", count, ct);

    public Task NotifyNotesUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("NotesUpdated", count, ct);

    public Task NotifyComparisonsUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("ComparisonsUpdated", count, ct);

    public Task NotifyPeriodSummariesUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("PeriodSummariesUpdated", count, ct);

    public Task NotifyEventLogsUpdatedAsync(int count, CancellationToken ct = default)
        => _hubContext.Clients.All.SendAsync("EventLogsUpdated", count, ct);
}
