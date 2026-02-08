using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that periodically fetches glucose data from LibreLink Up
/// and stores it in the SQL Server database.
/// </summary>
public class GlucoseFetchService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GlucoseFetchService> _logger;
    private readonly INotificationService _notifications;

    public GlucoseFetchService(
        IServiceProvider serviceProvider,
        ILogger<GlucoseFetchService> logger,
        INotificationService notifications)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GlucoseFetchService started.");

        // Wait for DB to be ready on first start
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan interval = TimeSpan.FromMinutes(5);

            try
            {
                interval = await FetchAndStoreAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
            {
                _logger.LogWarning("LibreLink credentials not configured yet. Waiting...");
                interval = TimeSpan.FromSeconds(30);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching glucose data.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    /// <summary>
    /// Manually trigger a glucose data sync from LibreLink Up.
    /// Can be called from an API controller.
    /// </summary>
    public async Task<(int inserted, string message)> TriggerSyncAsync()
    {
        _logger.LogInformation("Manual glucose sync triggered.");
        try
        {
            var interval = await FetchAndStoreAsync();
            return (0, "Glucose sync completed successfully."); // inserted count is logged internally
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            return (0, "LibreLink credentials not configured.");
        }
    }

    private async Task<TimeSpan> FetchAndStoreAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var client = scope.ServiceProvider.GetRequiredService<LibreLinkClient>();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

        // Load settings from DB
        var settings = await settingsService.GetLibreSettingsAsync();

        if (!settings.IsConfigured)
        {
            throw new InvalidOperationException("LibreLink credentials are not configured.");
        }

        // Configure the client with DB-stored credentials
        client.Configure(
            settings.Email,
            settings.Password,
            string.IsNullOrEmpty(settings.PatientId) ? null : settings.PatientId,
            settings.Region,
            settings.Version
        );

        // Find the latest reading already stored in the database
        var latestTimestamp = await db.GlucoseReadings
            .OrderByDescending(r => r.Timestamp)
            .Select(r => (DateTime?)r.Timestamp)
            .FirstOrDefaultAsync();

        if (latestTimestamp.HasValue)
        {
            _logger.LogInformation("Latest reading in DB: {Timestamp:u}. Fetching newer data from LibreLink Up...",
                latestTimestamp.Value);
        }
        else
        {
            _logger.LogInformation("No readings in DB yet. Fetching all available data from LibreLink Up...");
        }

        var graphData = await client.GetGraphDataAsync();
        if (graphData == null)
        {
            _logger.LogWarning("No graph data received.");
            return TimeSpan.FromMinutes(settings.FetchIntervalMinutes);
        }

        var patientId = graphData.Connection?.PatientId;
        var readings = client.ParseReadings(graphData, patientId);

        _logger.LogInformation("Received {Count} readings from LibreLink API.", readings.Count);

        // Filter to only readings newer than what we already have
        var newReadings = latestTimestamp.HasValue
            ? readings.Where(r => r.Timestamp > latestTimestamp.Value).ToList()
            : readings;

        if (newReadings.Count == 0)
        {
            _logger.LogInformation("No new readings since {Timestamp:u}.", latestTimestamp);
            return TimeSpan.FromMinutes(settings.FetchIntervalMinutes);
        }

        _logger.LogInformation("{Count} readings are newer than latest DB entry.", newReadings.Count);

        // Batch-check for any remaining duplicates (edge case: same timestamp, different patient)
        int inserted = 0;
        foreach (var reading in newReadings)
        {
            var exists = await db.GlucoseReadings.AnyAsync(r =>
                r.PatientId == reading.PatientId &&
                r.Timestamp == reading.Timestamp);

            if (!exists)
            {
                db.GlucoseReadings.Add(reading);
                inserted++;
            }
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Inserted {Count} new readings into database.", inserted);

            // Notify all connected UI clients to refresh their data
            await _notifications.NotifyNewGlucoseDataAsync(inserted);
            _logger.LogInformation("Notified connected clients of {Count} new readings.", inserted);

            // Recalculate any events whose time period overlaps with the new data
            await RecalculateImpactedEventsAsync(db, newReadings);
        }
        else
        {
            _logger.LogInformation("All {Count} readings were duplicates — nothing to insert.", newReadings.Count);
        }

        return TimeSpan.FromMinutes(settings.FetchIntervalMinutes);
    }

    /// <summary>
    /// Find glucose events whose period overlaps with newly arrived readings,
    /// recompute their glucose stats using the domain service, and mark them for AI re-analysis if stats changed.
    /// </summary>
    private async Task RecalculateImpactedEventsAsync(GlucoseDbContext db, IList<GlucoseReading> newReadings)
    {
        if (newReadings.Count == 0) return;

        var newMin = newReadings.Min(r => r.Timestamp);
        var newMax = newReadings.Max(r => r.Timestamp);

        // Find events whose period overlaps with the new data range
        var impactedEvents = await db.GlucoseEvents
            .Where(e => e.PeriodStart <= newMax && e.PeriodEnd >= newMin)
            .ToListAsync();

        if (impactedEvents.Count == 0) return;

        _logger.LogInformation(
            "Found {Count} event(s) overlapping with new glucose data ({Min:u} – {Max:u}). Recalculating stats...",
            impactedEvents.Count, newMin, newMax);

        int recalculated = 0;

        foreach (var evt in impactedEvents)
        {
            var readings = await db.GlucoseReadings
                .Where(r => r.Timestamp >= evt.PeriodStart && r.Timestamp <= evt.PeriodEnd)
                .OrderBy(r => r.Timestamp)
                .ToListAsync();

            // Use domain service for stats computation
            var stats = GlucoseStatsCalculator.ComputeEventStats(readings, evt.EventTimestamp);

            // Check if stats actually changed
            bool statsChanged = evt.ReadingCount != stats.ReadingCount
                || !GlucoseStatsCalculator.NullableDoubleEquals(evt.GlucoseAtEvent, stats.GlucoseAtEvent)
                || !GlucoseStatsCalculator.NullableDoubleEquals(evt.GlucoseMin, stats.Min)
                || !GlucoseStatsCalculator.NullableDoubleEquals(evt.GlucoseMax, stats.Max)
                || !GlucoseStatsCalculator.NullableDoubleEquals(evt.GlucoseAvg, stats.Avg)
                || !GlucoseStatsCalculator.NullableDoubleEquals(evt.GlucoseSpike, stats.Spike);

            if (statsChanged)
            {
                _logger.LogInformation(
                    "Event '{Title}' (ID={Id}) stats changed: readings {OldCount}→{NewCount}, " +
                    "atEvent {OldAt}→{NewAt}, spike {OldSpike}→{NewSpike}. Marking for re-analysis.",
                    evt.NoteTitle, evt.Id,
                    evt.ReadingCount, stats.ReadingCount,
                    evt.GlucoseAtEvent?.ToString("F0") ?? "N/A", stats.GlucoseAtEvent?.ToString("F0") ?? "N/A",
                    evt.GlucoseSpike?.ToString("F1") ?? "N/A", stats.Spike?.ToString("F1") ?? "N/A");

                evt.ReadingCount = stats.ReadingCount;
                evt.GlucoseAtEvent = stats.GlucoseAtEvent;
                evt.GlucoseMin = stats.Min;
                evt.GlucoseMax = stats.Max;
                evt.GlucoseAvg = stats.Avg;
                evt.GlucoseSpike = stats.Spike;
                evt.PeakTime = stats.PeakTime;
                evt.IsProcessed = false;
                evt.UpdatedAt = DateTime.UtcNow;
                recalculated++;
            }
        }

        if (recalculated > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("{Count} event(s) recalculated and queued for AI re-analysis.", recalculated);
            await _notifications.NotifyEventsUpdatedAsync(recalculated);
        }
    }
}
