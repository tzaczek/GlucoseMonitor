using System.Globalization;
using GlucoseAPI.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Background service that performs a full SQL Server database backup once per day.
/// The .bak file is written to the /backup/db/ directory (shared volume between the
/// API and SQL Server containers). Old backups beyond the retention period are cleaned up.
/// Also exposes status information and a manual trigger method.
/// </summary>
public class DatabaseBackupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly IConfiguration _configuration;

    private const string SubFolder = "db";
    private const int RetentionDays = 7;

    // ── Status tracking ──────────────────────────────────────
    private DateTime? _lastBackupUtc;
    private string? _lastBackupFile;
    private long? _lastBackupSizeBytes;
    private bool _isRunning;
    private string? _lastError;

    public DatabaseBackupService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseBackupService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>Get the current backup status.</summary>
    public BackupStatusDto GetStatus()
    {
        var backups = ListExistingBackups();
        return new BackupStatusDto
        {
            LastBackupUtc = _lastBackupUtc,
            LastBackupFile = _lastBackupFile,
            LastBackupSizeBytes = _lastBackupSizeBytes,
            IsRunning = _isRunning,
            LastError = _lastError,
            BackupCount = backups.Count,
            TotalSizeBytes = backups.Sum(b => b.Size),
            Backups = backups
        };
    }

    /// <summary>Manually trigger a database backup.</summary>
    public async Task<string> TriggerBackupAsync(CancellationToken ct)
    {
        if (_isRunning)
            return "A backup is already in progress.";

        _logger.LogInformation("Manual database backup triggered.");
        await RunBackupAsync(ct);

        return _lastError != null
            ? $"Backup failed: {_lastError}"
            : $"Backup completed successfully → {_lastBackupFile}";
    }

    /// <summary>Restore the database from a backup file. This is a destructive operation.</summary>
    public async Task<string> RestoreFromBackupAsync(string fileName, CancellationToken ct)
    {
        if (_isRunning)
            return "A backup/restore operation is already in progress.";

        // Validate filename (prevent path traversal)
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
            return "Invalid backup file name.";

        var dbBackupDir = GetBackupDir();
        var backupPath = Path.Combine(dbBackupDir, fileName);

        if (!File.Exists(backupPath))
            return $"Backup file not found: {fileName}";

        _logger.LogWarning("Database restore requested from: {File}", fileName);
        _isRunning = true;
        _lastError = null;

        try
        {
            // We must connect to 'master' (not GlucoseDb) to restore.
            // Build the master connection string from the existing one.
            using var scope = _serviceProvider.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var connStr = config.GetConnectionString("DefaultConnection") ?? "";
            var masterConnStr = new SqlConnectionStringBuilder(connStr)
            {
                InitialCatalog = "master"
            }.ConnectionString;

            await using var conn = new SqlConnection(masterConnStr);
            await conn.OpenAsync(ct);

            // 1. Kick all users off GlucoseDb and set to single-user
            _logger.LogInformation("Setting GlucoseDb to SINGLE_USER for restore...");
            await using (var cmd1 = new SqlCommand(
                "ALTER DATABASE [GlucoseDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;", conn))
            {
                cmd1.CommandTimeout = 60;
                await cmd1.ExecuteNonQueryAsync(ct);
            }

            // 2. Restore
            _logger.LogInformation("Restoring database from {File}...", fileName);
            var restoreSql = $"RESTORE DATABASE [GlucoseDb] FROM DISK = N'{backupPath}' WITH REPLACE;";
            await using (var cmd2 = new SqlCommand(restoreSql, conn))
            {
                cmd2.CommandTimeout = 300; // 5 minutes
                await cmd2.ExecuteNonQueryAsync(ct);
            }

            // 3. Set back to multi-user
            _logger.LogInformation("Setting GlucoseDb back to MULTI_USER...");
            await using (var cmd3 = new SqlCommand(
                "ALTER DATABASE [GlucoseDb] SET MULTI_USER;", conn))
            {
                cmd3.CommandTimeout = 30;
                await cmd3.ExecuteNonQueryAsync(ct);
            }

            _logger.LogInformation("Database restored successfully from {File}.", fileName);
            return $"Database restored successfully from {fileName}. The application will use the restored data.";
        }
        catch (Exception ex)
        {
            _lastError = $"Restore failed: {ex.Message}";
            _logger.LogError(ex, "Database restore failed from {File}.", fileName);

            // Try to set back to multi-user even on failure
            try
            {
                using var scope2 = _serviceProvider.CreateScope();
                var config2 = scope2.ServiceProvider.GetRequiredService<IConfiguration>();
                var connStr2 = config2.GetConnectionString("DefaultConnection") ?? "";
                var masterConnStr2 = new SqlConnectionStringBuilder(connStr2)
                {
                    InitialCatalog = "master"
                }.ConnectionString;

                await using var conn2 = new SqlConnection(masterConnStr2);
                await conn2.OpenAsync(ct);
                await using var cmd = new SqlCommand(
                    "ALTER DATABASE [GlucoseDb] SET MULTI_USER;", conn2);
                cmd.CommandTimeout = 30;
                await cmd.ExecuteNonQueryAsync(ct);
            }
            catch { /* best effort */ }

            return _lastError;
        }
        finally
        {
            _isRunning = false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DatabaseBackupService started. Running once per day, retaining {Days} days.", RetentionDays);

        // Detect existing backups on startup
        DetectLastBackup();

        // Wait for SQL Server to be fully ready
        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunBackupAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in DatabaseBackupService.");
            }

            // Run once per day
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    private string GetBackupDir()
    {
        var backupRoot = _configuration["Backup:Path"];
        if (string.IsNullOrWhiteSpace(backupRoot))
            backupRoot = "/backup";
        return Path.Combine(backupRoot, SubFolder);
    }

    /// <summary>On startup, detect the most recent existing backup file.</summary>
    private void DetectLastBackup()
    {
        try
        {
            var dbBackupDir = GetBackupDir();
            if (!Directory.Exists(dbBackupDir)) return;

            var latest = Directory.GetFiles(dbBackupDir, "GlucoseDb_*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latest != null)
            {
                _lastBackupUtc = latest.LastWriteTimeUtc;
                _lastBackupFile = latest.Name;
                _lastBackupSizeBytes = latest.Length;
                _logger.LogInformation("Detected existing database backup: {File} ({Size} bytes, {Date})",
                    latest.Name, latest.Length, latest.LastWriteTimeUtc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to detect existing backups: {Msg}", ex.Message);
        }
    }

    private async Task RunBackupAsync(CancellationToken ct)
    {
        var dbBackupDir = GetBackupDir();

        // Ensure directory exists and is writable by SQL Server (mssql user).
        // The API container runs as root, so it can set permissions.
        try
        {
            if (!Directory.Exists(dbBackupDir))
                Directory.CreateDirectory(dbBackupDir);

            // Make world-writable so the mssql user in the sqlserver container can write .bak files
            if (!OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("chmod", $"777 {dbBackupDir}")?.WaitForExit(3000);
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning("Cannot create/configure database backup directory '{Path}': {Msg}", dbBackupDir, ex.Message);
            return;
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var fileName = $"GlucoseDb_{timestamp}.bak";
        var backupPath = Path.Combine(dbBackupDir, fileName);

        _logger.LogInformation("Starting SQL Server database backup → {Path}", backupPath);
        _isRunning = true;
        _lastError = null;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

            var sql = $"BACKUP DATABASE [GlucoseDb] TO DISK = N'{backupPath}' WITH FORMAT, INIT, COMPRESSION, NAME = N'GlucoseDb-Daily-{timestamp}'";

            await db.Database.ExecuteSqlRawAsync(sql, ct);

            // Update status
            var fileInfo = new FileInfo(backupPath);
            _lastBackupUtc = DateTime.UtcNow;
            _lastBackupFile = fileName;
            _lastBackupSizeBytes = fileInfo.Exists ? fileInfo.Length : null;

            _logger.LogInformation("Database backup completed successfully → {File}", fileName);
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogError(ex, "Database backup failed.");
        }
        finally
        {
            _isRunning = false;
        }

        // Clean up old backups
        CleanupOldBackups(dbBackupDir);
    }

    private List<BackupFileDto> ListExistingBackups()
    {
        var result = new List<BackupFileDto>();
        try
        {
            var dbBackupDir = GetBackupDir();
            if (!Directory.Exists(dbBackupDir)) return result;

            foreach (var file in Directory.GetFiles(dbBackupDir, "GlucoseDb_*.bak"))
            {
                var fi = new FileInfo(file);
                result.Add(new BackupFileDto
                {
                    FileName = fi.Name,
                    Size = fi.Length,
                    CreatedUtc = fi.LastWriteTimeUtc
                });
            }

            result.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
        }
        catch { /* ignore listing errors */ }
        return result;
    }

    private void CleanupOldBackups(string dbBackupDir)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

            foreach (var file in Directory.GetFiles(dbBackupDir, "GlucoseDb_*.bak"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var tsPart = name.Replace("GlucoseDb_", "");
                if (DateTime.TryParseExact(tsPart, "yyyy-MM-dd_HH-mm-ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                {
                    if (fileDate < cutoff)
                    {
                        File.Delete(file);
                        _logger.LogDebug("Deleted old database backup: {File}", Path.GetFileName(file));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to clean up old database backups: {Msg}", ex.Message);
        }
    }
}

// ── DTOs ─────────────────────────────────────────────────────

public class BackupStatusDto
{
    public DateTime? LastBackupUtc { get; set; }
    public string? LastBackupFile { get; set; }
    public long? LastBackupSizeBytes { get; set; }
    public bool IsRunning { get; set; }
    public string? LastError { get; set; }
    public int BackupCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<BackupFileDto> Backups { get; set; } = new();
}

public class BackupFileDto
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedUtc { get; set; }
}
