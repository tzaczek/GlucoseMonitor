using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GlucoseAPI.Services;

/// <summary>
/// Internal model for a note read from the Samsung Notes SQLite database.
/// </summary>
public class SamsungNoteRaw
{
    public string Uuid { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? TextContent { get; set; }
    public long ModifiedTime { get; set; }
    public bool IsDeleted { get; set; }
    public string? FolderName { get; set; }
    public string? ThumbnailPath { get; set; }
}

/// <summary>
/// Reads Samsung Notes data from the locally synced filesystem.
/// Expects the Samsung Notes LocalState directory to be mounted at a known path.
/// </summary>
public class SamsungNotesReader
{
    private readonly ILogger<SamsungNotesReader> _logger;
    private readonly string _basePath;

    public SamsungNotesReader(ILogger<SamsungNotesReader> logger, IConfiguration configuration)
    {
        _logger = logger;
        _basePath = configuration.GetValue<string>("SamsungNotes:DataPath") ?? "/samsung-notes";
    }

    /// <summary>Check if the Samsung Notes data directory is available.</summary>
    public bool IsAvailable()
    {
        if (!Directory.Exists(_basePath))
            return false;

        var dbPath = FindDatabaseFile();
        return dbPath != null;
    }

    /// <summary>Find the SQLite database file in the data directory.</summary>
    public string? FindDatabaseFile()
    {
        if (!Directory.Exists(_basePath))
            return null;

        // Primary: Storage.sqlite (the actual Samsung Notes DB name)
        var primary = Path.Combine(_basePath, "Storage.sqlite");
        if (File.Exists(primary))
            return primary;

        // Fallback: search for any .sqlite or .db file
        try
        {
            var files = Directory.GetFiles(_basePath, "*.sqlite", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(_basePath, "*.db", SearchOption.TopDirectoryOnly))
                .ToArray();

            if (files.Length > 0)
            {
                _logger.LogInformation("Found database file: {Path}", files[0]);
                return files[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for database files in {Path}", _basePath);
        }

        return null;
    }

    /// <summary>Find the wdoc content directory.</summary>
    public string? FindWdocDirectory()
    {
        var path = Path.Combine(_basePath, "wdoc");
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Read all notes from the Samsung Notes SQLite database.
    /// Copies the database to a temp directory to avoid locking conflicts.
    /// </summary>
    public List<SamsungNoteRaw> ReadNotesFromDatabase()
    {
        var dbPath = FindDatabaseFile();
        if (dbPath == null)
        {
            _logger.LogWarning("No Samsung Notes database file found in {Path}", _basePath);
            return new List<SamsungNoteRaw>();
        }

        // Copy database files to temp directory to avoid locks with the Samsung Notes app
        var tempDir = Path.Combine(Path.GetTempPath(), "samsung-notes-sync");
        Directory.CreateDirectory(tempDir);

        var tempDb = Path.Combine(tempDir, Path.GetFileName(dbPath));

        try
        {
            File.Copy(dbPath, tempDb, overwrite: true);

            // Also copy WAL and SHM if they exist (required for WAL mode)
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            if (File.Exists(walPath)) File.Copy(walPath, tempDb + "-wal", overwrite: true);
            if (File.Exists(shmPath)) File.Copy(shmPath, tempDb + "-shm", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy database files for safe reading.");
            return new List<SamsungNoteRaw>();
        }

        try
        {
            return ReadFromDatabase(tempDb);
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (File.Exists(tempDb)) File.Delete(tempDb);
                if (File.Exists(tempDb + "-wal")) File.Delete(tempDb + "-wal");
                if (File.Exists(tempDb + "-shm")) File.Delete(tempDb + "-shm");
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private List<SamsungNoteRaw> ReadFromDatabase(string dbPath)
    {
        var notes = new List<SamsungNoteRaw>();
        var connectionString = $"Data Source={dbPath};Mode=ReadOnly";

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Check if we have the known Samsung Notes schema (NoteDB + CategoryDB)
        if (TableExists(connection, "NoteDB"))
        {
            return ReadFromKnownSchema(connection);
        }

        // Fallback: try to discover the schema dynamically
        _logger.LogInformation("NoteDB table not found, attempting schema discovery...");
        return ReadFromDiscoveredSchema(connection);
    }

    /// <summary>Read notes using the known Samsung Notes Windows app schema.</summary>
    private List<SamsungNoteRaw> ReadFromKnownSchema(SqliteConnection connection)
    {
        var notes = new List<SamsungNoteRaw>();

        // Samsung Notes uses CategoryTreeDB (not CategoryDB) for folder hierarchy
        var hasCategoryTree = TableExists(connection, "CategoryTreeDB");
        var hasCategoryDb = !hasCategoryTree && TableExists(connection, "CategoryDB");

        string query;
        if (hasCategoryTree)
        {
            query = @"SELECT n.UUID, n.Title, n.LastModifiedAt, n.DeletedStatus, 
                             ct.DisplayName AS FolderName, n.StrippedContent, n.ThumbnailPath
                      FROM NoteDB n 
                      LEFT JOIN CategoryTreeDB ct ON n.CategoryUUID = ct.UUID
                      ORDER BY n.LastModifiedAt DESC";
        }
        else if (hasCategoryDb)
        {
            query = @"SELECT n.UUID, n.Title, n.LastModifiedAt, n.DeletedStatus, 
                             c.DisplayName AS FolderName, n.StrippedContent, n.ThumbnailPath
                      FROM NoteDB n 
                      LEFT JOIN CategoryDB c ON n.CategoryUUID = c.UUID
                      ORDER BY n.LastModifiedAt DESC";
        }
        else
        {
            query = @"SELECT UUID, Title, LastModifiedAt, DeletedStatus, 
                             NULL AS FolderName, StrippedContent, ThumbnailPath
                      FROM NoteDB 
                      ORDER BY LastModifiedAt DESC";
        }

        var command = connection.CreateCommand();
        command.CommandText = query;

        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var uuid = reader["UUID"]?.ToString();
                if (string.IsNullOrEmpty(uuid)) continue;

                // Clean up system category names — only keep real user-created folders
                var folderName = reader["FolderName"]?.ToString();
                if (string.IsNullOrWhiteSpace(folderName) 
                    || folderName == "Folders"           // root / uncategorized
                    || folderName.Contains('\u200B'))     // contains zero-width spaces (system entries)
                {
                    folderName = null;
                }

                notes.Add(new SamsungNoteRaw
                {
                    Uuid = uuid,
                    Title = reader["Title"]?.ToString(),
                    ModifiedTime = reader["LastModifiedAt"] is long l ? l : 0,
                    IsDeleted = reader["DeletedStatus"] is long d && d != 0,
                    FolderName = folderName,
                    TextContent = reader["StrippedContent"]?.ToString(),
                    ThumbnailPath = reader["ThumbnailPath"]?.ToString()
                });
            }
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Error reading from NoteDB.");
        }

        _logger.LogInformation("Read {Count} notes from Samsung Notes NoteDB.", notes.Count);
        return notes;
    }

    /// <summary>Fallback: discover schema dynamically for different versions.</summary>
    private List<SamsungNoteRaw> ReadFromDiscoveredSchema(SqliteConnection connection)
    {
        var notes = new List<SamsungNoteRaw>();

        // Try known table name candidates
        var candidates = new[] { "Note", "Memo", "T_DOCUMENT", "note", "memo", "Notes", "notes" };
        string? tableName = null;

        foreach (var candidate in candidates)
        {
            if (TableExists(connection, candidate))
            {
                tableName = candidate;
                break;
            }
        }

        if (tableName == null)
        {
            _logger.LogWarning("Could not find a notes table. Available tables:");
            LogAllTables(connection);
            return notes;
        }

        // Get columns for the discovered table
        var columns = GetColumns(connection, tableName);
        var colLower = columns.Select(c => c.ToLowerInvariant()).ToList();

        _logger.LogInformation("Using table '{Table}' with columns: {Columns}", tableName, string.Join(", ", columns));

        // Map columns
        var uuidCol = columns.FirstOrDefault(c =>
            c.Equals("UUID", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("_id", StringComparison.OrdinalIgnoreCase));

        var titleCol = columns.FirstOrDefault(c =>
            c.Equals("Title", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("Subject", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("Name", StringComparison.OrdinalIgnoreCase));

        var modifiedCol = columns.FirstOrDefault(c =>
            c.Contains("Modified", StringComparison.OrdinalIgnoreCase) ||
            c.Contains("Updated", StringComparison.OrdinalIgnoreCase));

        var deletedCol = columns.FirstOrDefault(c =>
            c.Contains("Delete", StringComparison.OrdinalIgnoreCase));

        var contentCol = columns.FirstOrDefault(c =>
            c.Contains("Content", StringComparison.OrdinalIgnoreCase) ||
            c.Contains("Body", StringComparison.OrdinalIgnoreCase) ||
            c.Contains("Text", StringComparison.OrdinalIgnoreCase));

        if (uuidCol == null)
        {
            _logger.LogWarning("No UUID/ID column found in table {Table}.", tableName);
            return notes;
        }

        var selectCols = new List<string> { $"\"{uuidCol}\"" };
        if (titleCol != null) selectCols.Add($"\"{titleCol}\"");
        if (modifiedCol != null) selectCols.Add($"\"{modifiedCol}\"");
        if (deletedCol != null) selectCols.Add($"\"{deletedCol}\"");
        if (contentCol != null) selectCols.Add($"\"{contentCol}\"");

        var query = $"SELECT {string.Join(", ", selectCols)} FROM \"{tableName}\"";
        if (modifiedCol != null) query += $" ORDER BY \"{modifiedCol}\" DESC";

        var command = connection.CreateCommand();
        command.CommandText = query;

        try
        {
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var note = new SamsungNoteRaw
                {
                    Uuid = reader[uuidCol]?.ToString() ?? ""
                };

                if (titleCol != null) note.Title = reader[titleCol]?.ToString();
                if (contentCol != null) note.TextContent = reader[contentCol]?.ToString();

                if (modifiedCol != null)
                {
                    var val = reader[modifiedCol];
                    if (val is long ml) note.ModifiedTime = ml;
                    else if (long.TryParse(val?.ToString(), out var parsed)) note.ModifiedTime = parsed;
                }

                if (deletedCol != null)
                {
                    var val = reader[deletedCol];
                    note.IsDeleted = val is long dl && dl != 0;
                }

                if (!string.IsNullOrEmpty(note.Uuid))
                    notes.Add(note);
            }
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Error reading from table {Table}.", tableName);
        }

        _logger.LogInformation("Read {Count} notes from table '{Table}'.", notes.Count, tableName);
        return notes;
    }

    /// <summary>Check if a note has media files in the wdoc directory.</summary>
    public bool HasMediaFiles(string noteUuid)
    {
        var wdocDir = FindWdocDirectory();
        if (wdocDir == null) return false;

        var mediaDir = Path.Combine(wdocDir, noteUuid, "media");
        return Directory.Exists(mediaDir) && Directory.GetFiles(mediaDir).Length > 0;
    }

    /// <summary>Check if a note has a preview image.</summary>
    public bool HasPreviewImage(string noteUuid)
    {
        var wdocDir = FindWdocDirectory();
        if (wdocDir == null) return false;

        // Check in wdoc directory
        var previewPath = Path.Combine(wdocDir, noteUuid, "preview.jpg");
        if (File.Exists(previewPath)) return true;

        // Also check Thumbnail directory
        var thumbDir = Path.Combine(_basePath, "Thumbnail");
        if (Directory.Exists(thumbDir))
        {
            var thumbFiles = Directory.GetFiles(thumbDir, $"*{noteUuid}*");
            return thumbFiles.Length > 0;
        }

        return false;
    }

    /// <summary>Get the preview image bytes for a note.</summary>
    public byte[]? GetPreviewImage(string noteUuid)
    {
        var wdocDir = FindWdocDirectory();
        if (wdocDir != null)
        {
            var previewPath = Path.Combine(wdocDir, noteUuid, "preview.jpg");
            if (File.Exists(previewPath))
            {
                try { return File.ReadAllBytes(previewPath); }
                catch { /* fall through */ }
            }
        }

        // Try Thumbnail directory
        var thumbDir = Path.Combine(_basePath, "Thumbnail");
        if (Directory.Exists(thumbDir))
        {
            var thumbFiles = Directory.GetFiles(thumbDir, $"*{noteUuid}*");
            if (thumbFiles.Length > 0)
            {
                try { return File.ReadAllBytes(thumbFiles[0]); }
                catch { /* fall through */ }
            }
        }

        return null;
    }

    /// <summary>List media files for a note.</summary>
    public List<string> GetMediaFiles(string noteUuid)
    {
        var wdocDir = FindWdocDirectory();
        if (wdocDir == null) return new List<string>();

        var mediaDir = Path.Combine(wdocDir, noteUuid, "media");
        if (!Directory.Exists(mediaDir)) return new List<string>();

        return Directory.GetFiles(mediaDir)
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Select(f => f!)
            .ToList();
    }

    /// <summary>Get media file bytes.</summary>
    public byte[]? GetMediaFile(string noteUuid, string fileName)
    {
        var wdocDir = FindWdocDirectory();
        if (wdocDir == null) return null;

        // Sanitize filename to prevent directory traversal
        fileName = Path.GetFileName(fileName);
        var filePath = Path.Combine(wdocDir, noteUuid, "media", fileName);

        if (!File.Exists(filePath)) return null;

        try { return File.ReadAllBytes(filePath); }
        catch { return null; }
    }

    /// <summary>
    /// Extract text content from a note's wdoc directory (fallback when StrippedContent is empty).
    /// </summary>
    public string? ExtractNoteContentFromWdoc(string noteUuid)
    {
        var wdocDir = FindWdocDirectory();
        if (wdocDir == null) return null;

        var noteDir = Path.Combine(wdocDir, noteUuid);
        if (!Directory.Exists(noteDir)) return null;

        // Try note.note (binary format)
        var noteFile = Path.Combine(noteDir, "note.note");
        if (File.Exists(noteFile))
            return ExtractTextFromBinary(noteFile);

        // Try content.xml (older format)
        var xmlFile = Path.Combine(noteDir, "content.xml");
        if (File.Exists(xmlFile))
            return ExtractTextFromXml(xmlFile);

        return null;
    }

    private string? ExtractTextFromBinary(string filePath)
    {
        try
        {
            var data = File.ReadAllBytes(filePath);
            if (data.Length == 0) return null;

            var rawText = Encoding.UTF8.GetString(data);

            // Extract sequences of printable characters (at least 4 chars)
            var matches = Regex.Matches(rawText, @"[\p{L}\p{N}\p{P}\p{Z}\p{S}]{4,}");
            if (matches.Count == 0) return null;

            var sb = new StringBuilder();
            foreach (Match match in matches)
            {
                var text = match.Value.Trim();
                if (!string.IsNullOrWhiteSpace(text) && !IsLikelyBinaryGarbage(text))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text);
                }
            }

            var result = Regex.Replace(sb.ToString().Trim(), @"\s{2,}", " ");
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch { return null; }
    }

    private string? ExtractTextFromXml(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            var text = Regex.Replace(content, @"<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch { return null; }
    }

    private static bool IsLikelyBinaryGarbage(string text)
    {
        if (text.Length < 4) return true;
        var letterCount = text.Count(char.IsLetter);
        return (double)letterCount / text.Length < 0.3;
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static List<string> GetColumns(SqliteConnection connection, string tableName)
    {
        var cols = new List<string>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            cols.Add(reader.GetString(1));
        return cols;
    }

    private void LogAllTables(SqliteConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var tables = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        _logger.LogInformation("Available tables: {Tables}", string.Join(", ", tables));
    }
}
