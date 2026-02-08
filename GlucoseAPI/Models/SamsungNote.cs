using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlucoseAPI.Models;

[Table("SamsungNotes")]
public class SamsungNote
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>UUID from Samsung Notes (primary identifier).</summary>
    [MaxLength(200)]
    public string Uuid { get; set; } = string.Empty;

    /// <summary>Note title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Extracted text content from the note.</summary>
    public string? TextContent { get; set; }

    /// <summary>When the note was last modified in Samsung Notes (UTC).</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>Whether the note is deleted in Samsung Notes.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Folder/category name if available.</summary>
    [MaxLength(500)]
    public string? FolderName { get; set; }

    /// <summary>Whether the note has media files (images, audio).</summary>
    public bool HasMedia { get; set; }

    /// <summary>Whether a preview image exists.</summary>
    public bool HasPreview { get; set; }

    /// <summary>When this record was first imported into our database (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this record was last updated in our database (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>DTO for returning notes to the UI.</summary>
public class SamsungNoteDto
{
    public int Id { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? TextContent { get; set; }
    public DateTime ModifiedAt { get; set; }
    public bool IsDeleted { get; set; }
    public string? FolderName { get; set; }
    public bool HasMedia { get; set; }
    public bool HasPreview { get; set; }
}
