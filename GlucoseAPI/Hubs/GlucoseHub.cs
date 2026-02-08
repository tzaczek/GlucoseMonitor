using Microsoft.AspNetCore.SignalR;

namespace GlucoseAPI.Hubs;

/// <summary>
/// SignalR hub for real-time glucose data updates.
/// Clients connect here to receive notifications when new readings are available.
/// </summary>
public class GlucoseHub : Hub
{
    private readonly ILogger<GlucoseHub> _logger;

    public GlucoseHub(ILogger<GlucoseHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
