using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that periodically exports glucose readings, events,
/// and analysis history to local JSON/CSV files as a backup.
/// Files are written to the path configured by <c>Backup:Path</c> (env var or appsettings).
/// Each run creates timestamped files; the service also maintains a "latest" copy.
/// </summary>
public class DataBackupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DataBackupService> _logger;
    private readonly IConfiguration _configuration;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DataBackupService(
        IServiceProvider serviceProvider,
        ILogger<DataBackupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataBackupService started.");

        // Wait for other services to initialise & first data to arrive
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunBackupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in DataBackupService.");
            }

            // Run every 6 hours
            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private string GetBackupPath()
    {
        var path = _configuration["Backup:Path"];
        return string.IsNullOrWhiteSpace(path) ? "/backup" : path;
    }

    private async Task RunBackupAsync(CancellationToken ct)
    {
        var backupRoot = GetBackupPath();
        if (!Directory.Exists(backupRoot))
        {
            try
            {
                Directory.CreateDirectory(backupRoot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Cannot create backup directory '{Path}': {Msg}", backupRoot, ex.Message);
                return;
            }
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var snapshotDir = Path.Combine(backupRoot, timestamp);
        var latestDir = Path.Combine(backupRoot, "latest");

        Directory.CreateDirectory(snapshotDir);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

        // ── 1. Glucose Readings ─────────────────────────────────
        await BackupGlucoseReadingsAsync(db, snapshotDir, ct);

        // ── 2. Glucose Events ───────────────────────────────────
        await BackupGlucoseEventsAsync(db, snapshotDir, ct);

        // ── 3. Analysis History ─────────────────────────────────
        await BackupAnalysisHistoryAsync(db, snapshotDir, ct);

        // ── 4. Daily Summaries ───────────────────────────────────
        await BackupDailySummariesAsync(db, snapshotDir, ct);

        // ── 5. Daily Summary Snapshots ───────────────────────────
        await BackupDailySummarySnapshotsAsync(db, snapshotDir, ct);

        // ── 6. Copy to "latest" ─────────────────────────────────
        CopyToLatest(snapshotDir, latestDir);

        // ── 7. Clean up old snapshots (keep last 14 days) ───────
        CleanupOldSnapshots(backupRoot, maxAgeDays: 14);

        _logger.LogInformation("Backup completed → {Path}", snapshotDir);
    }

    // ────────────────────────────────────────────────────────────
    // Glucose Readings (JSON + CSV)
    // ────────────────────────────────────────────────────────────

    private async Task BackupGlucoseReadingsAsync(GlucoseDbContext db, string dir, CancellationToken ct)
    {
        var readings = await db.GlucoseReadings
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        if (readings.Count == 0) return;

        // JSON
        var jsonPath = Path.Combine(dir, "glucose_readings.json");
        var jsonData = readings.Select(r => new
        {
            r.Id,
            r.Value,
            Timestamp = r.Timestamp.ToString("o"),
            r.TrendArrow,
            r.IsHigh,
            r.IsLow,
            r.PatientId
        });
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonData, JsonOpts), ct);

        // CSV
        var csvPath = Path.Combine(dir, "glucose_readings.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Id,Value,Timestamp,TrendArrow,IsHigh,IsLow,PatientId");
        foreach (var r in readings)
        {
            sb.AppendLine($"{r.Id},{r.Value},{r.Timestamp:o},{r.TrendArrow},{r.IsHigh},{r.IsLow},{Escape(r.PatientId)}");
        }
        await File.WriteAllTextAsync(csvPath, sb.ToString(), ct);

        _logger.LogInformation("Backed up {Count} glucose readings.", readings.Count);
    }

    // ────────────────────────────────────────────────────────────
    // Glucose Events (JSON)
    // ────────────────────────────────────────────────────────────

    private async Task BackupGlucoseEventsAsync(GlucoseDbContext db, string dir, CancellationToken ct)
    {
        var events = await db.GlucoseEvents
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        if (events.Count == 0) return;

        var jsonPath = Path.Combine(dir, "glucose_events.json");
        var jsonData = events.Select(e => new
        {
            e.Id,
            e.SamsungNoteId,
            e.NoteUuid,
            e.NoteTitle,
            e.NoteContent,
            EventTimestamp = e.EventTimestamp.ToString("o"),
            PeriodStart = e.PeriodStart.ToString("o"),
            PeriodEnd = e.PeriodEnd.ToString("o"),
            e.ReadingCount,
            e.GlucoseAtEvent,
            e.GlucoseMin,
            e.GlucoseMax,
            e.GlucoseAvg,
            e.GlucoseSpike,
            PeakTime = e.PeakTime?.ToString("o"),
            e.AiAnalysis,
            e.AiClassification,
            e.IsProcessed,
            ProcessedAt = e.ProcessedAt?.ToString("o"),
            CreatedAt = e.CreatedAt.ToString("o"),
            UpdatedAt = e.UpdatedAt.ToString("o")
        });
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonData, JsonOpts), ct);

        _logger.LogInformation("Backed up {Count} glucose events.", events.Count);
    }

    // ────────────────────────────────────────────────────────────
    // Analysis History (JSON)
    // ────────────────────────────────────────────────────────────

    private async Task BackupAnalysisHistoryAsync(GlucoseDbContext db, string dir, CancellationToken ct)
    {
        var history = await db.EventAnalysisHistory
            .OrderBy(h => h.AnalyzedAt)
            .ToListAsync(ct);

        if (history.Count == 0) return;

        var jsonPath = Path.Combine(dir, "analysis_history.json");
        var jsonData = history.Select(h => new
        {
            h.Id,
            h.GlucoseEventId,
            h.AiAnalysis,
            h.AiClassification,
            AnalyzedAt = h.AnalyzedAt.ToString("o"),
            PeriodStart = h.PeriodStart.ToString("o"),
            PeriodEnd = h.PeriodEnd.ToString("o"),
            h.ReadingCount,
            h.Reason
        });
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonData, JsonOpts), ct);

        _logger.LogInformation("Backed up {Count} analysis history entries.", history.Count);
    }

    // ────────────────────────────────────────────────────────────
    // Daily Summaries (JSON + CSV)
    // ────────────────────────────────────────────────────────────

    private async Task BackupDailySummariesAsync(GlucoseDbContext db, string dir, CancellationToken ct)
    {
        var summaries = await db.DailySummaries
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        if (summaries.Count == 0) return;

        // JSON
        var jsonPath = Path.Combine(dir, "daily_summaries.json");
        var jsonData = summaries.Select(s => new
        {
            s.Id,
            Date = s.Date.ToString("yyyy-MM-dd"),
            PeriodStartUtc = s.PeriodStartUtc.ToString("o"),
            PeriodEndUtc = s.PeriodEndUtc.ToString("o"),
            s.TimeZone,
            s.EventCount,
            s.EventIds,
            s.EventTitles,
            s.ReadingCount,
            s.GlucoseMin,
            s.GlucoseMax,
            s.GlucoseAvg,
            s.GlucoseStdDev,
            s.TimeInRange,
            s.TimeAboveRange,
            s.TimeBelowRange,
            s.AiAnalysis,
            s.AiClassification,
            s.IsProcessed,
            ProcessedAt = s.ProcessedAt?.ToString("o"),
            CreatedAt = s.CreatedAt.ToString("o"),
            UpdatedAt = s.UpdatedAt.ToString("o")
        });
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonData, JsonOpts), ct);

        // CSV
        var csvPath = Path.Combine(dir, "daily_summaries.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Id,Date,PeriodStartUtc,PeriodEndUtc,TimeZone,EventCount,ReadingCount,GlucoseMin,GlucoseMax,GlucoseAvg,GlucoseStdDev,TimeInRange,TimeAboveRange,TimeBelowRange,IsProcessed,ProcessedAt");
        foreach (var s in summaries)
        {
            sb.AppendLine(string.Join(",",
                s.Id,
                s.Date.ToString("yyyy-MM-dd"),
                s.PeriodStartUtc.ToString("o"),
                s.PeriodEndUtc.ToString("o"),
                Escape(s.TimeZone),
                s.EventCount,
                s.ReadingCount,
                s.GlucoseMin?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.GlucoseMax?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.GlucoseAvg?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.GlucoseStdDev?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.TimeInRange?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.TimeAboveRange?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.TimeBelowRange?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.IsProcessed,
                s.ProcessedAt?.ToString("o") ?? ""));
        }
        await File.WriteAllTextAsync(csvPath, sb.ToString(), ct);

        _logger.LogInformation("Backed up {Count} daily summaries.", summaries.Count);
    }

    // ────────────────────────────────────────────────────────────
    // Daily Summary Snapshots (JSON)
    // ────────────────────────────────────────────────────────────

    private async Task BackupDailySummarySnapshotsAsync(GlucoseDbContext db, string dir, CancellationToken ct)
    {
        var snapshots = await db.DailySummarySnapshots
            .OrderBy(s => s.GeneratedAt)
            .ToListAsync(ct);

        if (snapshots.Count == 0) return;

        var jsonPath = Path.Combine(dir, "daily_summary_snapshots.json");
        var jsonData = snapshots.Select(s => new
        {
            s.Id,
            s.DailySummaryId,
            Date = s.Date.ToString("yyyy-MM-dd"),
            GeneratedAt = s.GeneratedAt.ToString("o"),
            s.Trigger,
            DataStartUtc = s.DataStartUtc.ToString("o"),
            DataEndUtc = s.DataEndUtc.ToString("o"),
            FirstReadingUtc = s.FirstReadingUtc?.ToString("o"),
            LastReadingUtc = s.LastReadingUtc?.ToString("o"),
            s.TimeZone,
            s.EventCount,
            s.EventIds,
            s.EventTitles,
            s.ReadingCount,
            s.GlucoseMin,
            s.GlucoseMax,
            s.GlucoseAvg,
            s.GlucoseStdDev,
            s.TimeInRange,
            s.TimeAboveRange,
            s.TimeBelowRange,
            s.AiAnalysis,
            s.AiClassification,
            s.IsProcessed
        });
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(jsonData, JsonOpts), ct);

        _logger.LogInformation("Backed up {Count} daily summary snapshots.", snapshots.Count);
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    /// <summary>Copy all files from a snapshot directory to the "latest" directory.</summary>
    private void CopyToLatest(string sourceDir, string latestDir)
    {
        try
        {
            if (Directory.Exists(latestDir))
                Directory.Delete(latestDir, true);

            Directory.CreateDirectory(latestDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(latestDir, Path.GetFileName(file));
                File.Copy(file, dest, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to update 'latest' backup: {Msg}", ex.Message);
        }
    }

    /// <summary>Remove snapshot directories older than <paramref name="maxAgeDays"/> days.</summary>
    private void CleanupOldSnapshots(string backupRoot, int maxAgeDays)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

            foreach (var dir in Directory.GetDirectories(backupRoot))
            {
                var name = Path.GetFileName(dir);
                if (name == "latest") continue;

                // Try parse the directory name as a timestamp
                if (DateTime.TryParseExact(name, "yyyy-MM-dd_HH-mm-ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var dirDate))
                {
                    if (dirDate < cutoff)
                    {
                        Directory.Delete(dir, true);
                        _logger.LogDebug("Deleted old backup: {Dir}", name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to clean up old backups: {Msg}", ex.Message);
        }
    }

    /// <summary>Escape a string for CSV output.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
