using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that periodically syncs Samsung Notes from the local filesystem
/// into the SQL Server database.
/// </summary>
public class SamsungNotesSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SamsungNotesSyncService> _logger;
    private readonly INotificationService _notifications;
    private readonly int _syncIntervalMinutes;

    public SamsungNotesSyncService(
        IServiceProvider serviceProvider,
        ILogger<SamsungNotesSyncService> logger,
        INotificationService notifications,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _notifications = notifications;
        _syncIntervalMinutes = configuration.GetValue("SamsungNotes:SyncIntervalMinutes", 10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SamsungNotesSyncService started. Sync interval: {Interval} minutes.", _syncIntervalMinutes);

        // Wait for DB to be ready
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncNotesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Samsung Notes.");
            }

            await Task.Delay(TimeSpan.FromMinutes(_syncIntervalMinutes), stoppingToken);
        }
    }

    /// <summary>
    /// Manually trigger a Samsung Notes sync.
    /// Can be called from an API controller.
    /// </summary>
    public async Task<string> TriggerSyncAsync()
    {
        _logger.LogInformation("Manual Samsung Notes sync triggered.");
        await SyncNotesAsync();
        return "Samsung Notes sync completed.";
    }

    private async Task SyncNotesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<SamsungNotesReader>();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

        if (!reader.IsAvailable())
        {
            _logger.LogDebug("Samsung Notes data directory not available. Skipping sync.");
            return;
        }

        _logger.LogInformation("Starting Samsung Notes sync...");

        // Read notes metadata from Samsung Notes SQLite DB
        var rawNotes = reader.ReadNotesFromDatabase();

        if (rawNotes.Count == 0)
        {
            _logger.LogInformation("No notes found in Samsung Notes database.");
            return;
        }

        int inserted = 0, updated = 0;

        foreach (var raw in rawNotes)
        {
            // Convert Unix timestamp (milliseconds) to DateTime UTC
            var modifiedAt = raw.ModifiedTime > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(raw.ModifiedTime).UtcDateTime
                : DateTime.UtcNow;

            // Check if we already have this note
            var existing = await db.SamsungNotes.FirstOrDefaultAsync(n => n.Uuid == raw.Uuid);

            // Text content: prefer StrippedContent from DB, fallback to wdoc binary parsing
            var textContent = raw.TextContent?.Trim();
            if (string.IsNullOrWhiteSpace(textContent))
                textContent = reader.ExtractNoteContentFromWdoc(raw.Uuid);

            if (existing != null)
            {
                // Update if: note was modified, text content is missing, or folder name changed
                bool needsUpdate = modifiedAt > existing.ModifiedAt
                    || existing.TextContent == null
                    || existing.FolderName != raw.FolderName;

                if (needsUpdate)
                {
                    existing.Title = raw.Title ?? "Untitled";
                    existing.ModifiedAt = modifiedAt;
                    existing.IsDeleted = raw.IsDeleted;
                    existing.FolderName = raw.FolderName;
                    existing.TextContent = textContent ?? existing.TextContent;
                    existing.HasMedia = reader.HasMediaFiles(raw.Uuid);
                    existing.HasPreview = reader.HasPreviewImage(raw.Uuid);
                    existing.UpdatedAt = DateTime.UtcNow;
                    updated++;
                }
            }
            else
            {
                // New note — insert
                var note = new SamsungNote
                {
                    Uuid = raw.Uuid,
                    Title = raw.Title ?? "Untitled",
                    TextContent = textContent,
                    ModifiedAt = modifiedAt,
                    IsDeleted = raw.IsDeleted,
                    FolderName = raw.FolderName,
                    HasMedia = reader.HasMediaFiles(raw.Uuid),
                    HasPreview = reader.HasPreviewImage(raw.Uuid),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                db.SamsungNotes.Add(note);
                inserted++;
            }
        }

        if (inserted > 0 || updated > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("Samsung Notes sync complete: {Inserted} inserted, {Updated} updated.", inserted, updated);

            // Notify connected UI clients
            await _notifications.NotifyNotesUpdatedAsync(inserted + updated);
        }
        else
        {
            _logger.LogInformation("Samsung Notes sync complete: no changes detected.");
        }
    }
}
