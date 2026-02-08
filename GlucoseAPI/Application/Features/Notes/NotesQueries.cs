using GlucoseAPI.Data;
using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Application.Features.Notes;

// ── GetNotes ──────────────────────────────────────────────────

public record GetNotesQuery(string? Folder, string? Search, bool IncludeDeleted)
    : IRequest<List<SamsungNoteDto>>;

public class GetNotesHandler : IRequestHandler<GetNotesQuery, List<SamsungNoteDto>>
{
    private readonly GlucoseDbContext _db;

    public GetNotesHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<SamsungNoteDto>> Handle(GetNotesQuery request, CancellationToken ct)
    {
        var query = _db.SamsungNotes.AsQueryable();

        if (!request.IncludeDeleted)
            query = query.Where(n => !n.IsDeleted);
        if (!string.IsNullOrEmpty(request.Folder))
            query = query.Where(n => n.FolderName == request.Folder);
        if (!string.IsNullOrEmpty(request.Search))
            query = query.Where(n =>
                n.Title.Contains(request.Search) ||
                (n.TextContent != null && n.TextContent.Contains(request.Search)));

        var notes = await query.OrderByDescending(n => n.ModifiedAt).ToListAsync(ct);
        return notes.Select(MapToDto).ToList();
    }

    internal static SamsungNoteDto MapToDto(SamsungNote n) => new()
    {
        Id = n.Id,
        Uuid = n.Uuid,
        Title = n.Title,
        TextContent = n.TextContent,
        ModifiedAt = DateTime.SpecifyKind(n.ModifiedAt, DateTimeKind.Utc),
        IsDeleted = n.IsDeleted,
        FolderName = n.FolderName,
        HasMedia = n.HasMedia,
        HasPreview = n.HasPreview
    };
}

// ── GetNote ───────────────────────────────────────────────────

public record GetNoteQuery(int Id) : IRequest<SamsungNoteDto?>;

public class GetNoteHandler : IRequestHandler<GetNoteQuery, SamsungNoteDto?>
{
    private readonly GlucoseDbContext _db;

    public GetNoteHandler(GlucoseDbContext db) => _db = db;

    public async Task<SamsungNoteDto?> Handle(GetNoteQuery request, CancellationToken ct)
    {
        var note = await _db.SamsungNotes.FindAsync(new object[] { request.Id }, ct);
        return note == null ? null : GetNotesHandler.MapToDto(note);
    }
}

// ── GetNoteFolders ────────────────────────────────────────────

public record GetNoteFoldersQuery : IRequest<List<string>>;

public class GetNoteFoldersHandler : IRequestHandler<GetNoteFoldersQuery, List<string>>
{
    private readonly GlucoseDbContext _db;

    public GetNoteFoldersHandler(GlucoseDbContext db) => _db = db;

    public async Task<List<string>> Handle(GetNoteFoldersQuery request, CancellationToken ct)
    {
        return await _db.SamsungNotes
            .Where(n => n.FolderName != null && !n.IsDeleted)
            .Select(n => n.FolderName!)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync(ct);
    }
}

// ── GetNotesStatus ────────────────────────────────────────────

public record GetNotesStatusQuery : IRequest<NotesStatusResult>;

public record NotesStatusResult(bool IsAvailable, int NoteCount, string DataPath);

public class GetNotesStatusHandler : IRequestHandler<GetNotesStatusQuery, NotesStatusResult>
{
    private readonly GlucoseDbContext _db;
    private readonly SamsungNotesReader _reader;

    public GetNotesStatusHandler(GlucoseDbContext db, SamsungNotesReader reader)
    {
        _db = db;
        _reader = reader;
    }

    public async Task<NotesStatusResult> Handle(GetNotesStatusQuery request, CancellationToken ct)
    {
        var isAvailable = _reader.IsAvailable();
        var noteCount = await _db.SamsungNotes.CountAsync(n => !n.IsDeleted, ct);
        return new NotesStatusResult(isAvailable, noteCount, isAvailable ? "Connected" : "Not mounted");
    }
}

// ── GetNotePreview ────────────────────────────────────────────

public record GetNotePreviewQuery(int Id) : IRequest<NoteFileResult?>;

public record NoteFileResult(byte[] Data, string ContentType);

public class GetNotePreviewHandler : IRequestHandler<GetNotePreviewQuery, NoteFileResult?>
{
    private readonly GlucoseDbContext _db;
    private readonly SamsungNotesReader _reader;

    public GetNotePreviewHandler(GlucoseDbContext db, SamsungNotesReader reader)
    {
        _db = db;
        _reader = reader;
    }

    public async Task<NoteFileResult?> Handle(GetNotePreviewQuery request, CancellationToken ct)
    {
        var note = await _db.SamsungNotes.FindAsync(new object[] { request.Id }, ct);
        if (note == null) return null;

        var imageData = _reader.GetPreviewImage(note.Uuid);
        return imageData == null ? null : new NoteFileResult(imageData, "image/jpeg");
    }
}

// ── GetNoteMediaList ──────────────────────────────────────────

public record GetNoteMediaListQuery(int Id) : IRequest<NoteMediaListResult?>;

public record NoteMediaListResult(List<string> Files);

public class GetNoteMediaListHandler : IRequestHandler<GetNoteMediaListQuery, NoteMediaListResult?>
{
    private readonly GlucoseDbContext _db;
    private readonly SamsungNotesReader _reader;

    public GetNoteMediaListHandler(GlucoseDbContext db, SamsungNotesReader reader)
    {
        _db = db;
        _reader = reader;
    }

    public async Task<NoteMediaListResult?> Handle(GetNoteMediaListQuery request, CancellationToken ct)
    {
        var note = await _db.SamsungNotes.FindAsync(new object[] { request.Id }, ct);
        if (note == null) return null;

        var files = _reader.GetMediaFiles(note.Uuid);
        return new NoteMediaListResult(files);
    }
}

// ── GetNoteMediaFile ──────────────────────────────────────────

public record GetNoteMediaFileQuery(int Id, string FileName) : IRequest<NoteFileResult?>;

public class GetNoteMediaFileHandler : IRequestHandler<GetNoteMediaFileQuery, NoteFileResult?>
{
    private readonly GlucoseDbContext _db;
    private readonly SamsungNotesReader _reader;

    public GetNoteMediaFileHandler(GlucoseDbContext db, SamsungNotesReader reader)
    {
        _db = db;
        _reader = reader;
    }

    public async Task<NoteFileResult?> Handle(GetNoteMediaFileQuery request, CancellationToken ct)
    {
        var note = await _db.SamsungNotes.FindAsync(new object[] { request.Id }, ct);
        if (note == null) return null;

        var fileData = _reader.GetMediaFile(note.Uuid, request.FileName);
        if (fileData == null) return null;

        var ext = Path.GetExtension(request.FileName).ToLowerInvariant();
        var contentType = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".m4a" => "audio/mp4",
            ".amr" => "audio/amr",
            _ => "application/octet-stream"
        };

        return new NoteFileResult(fileData, contentType);
    }
}
