using System.Security.Cryptography;
using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>One attachment as a page shows it (never its bytes).</summary>
public sealed record AttachmentView(long Id, string FileName, long Size, DateTime Added, string Icon);

/// <summary>
/// Files attached to a note: any number of them, of any type, next to the text. They follow the access of the note (read: list and download; edit: add and remove) and are
/// deleted for good when the note is. Every one is only ever delivered as a download.
/// </summary>
public sealed class AttachmentService(AppDbContext db, NoteService notes)
{
    public const int MaxPerNote = 50;

    public async Task<List<AttachmentView>> ListAsync(long noteId)
    {
        var rows = await db.NoteAttachments.AsNoTracking().Where(a => a.NoteId == noteId).OrderBy(a => a.CreateDate).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.Size, a.CreateDate }).ToListAsync();
        return rows.Select(a => new AttachmentView(a.Id, a.FileName, a.Size, a.CreateDate, IconFor(a.FileName))).ToList();
    }

    public async Task<int> CountAsync(long noteId) => await db.NoteAttachments.CountAsync(a => a.NoteId == noteId);

    /// <summary>Adds a file to a note. Returns the new attachment, or the reason why not (empty file, too big, too many).</summary>
    public async Task<(AttachmentView? Added, string? Error)> AddAsync(long noteId, IFormFile file)
    {
        var problem = notes.CheckFile(file);
        if (problem is not null)
        {
            return (null, $"{CleanName(file.FileName)}: {problem}");
        }

        if (await CountAsync(noteId) >= MaxPerNote)
        {
            return (null, $"A note can carry up to {MaxPerNote} attachments.");
        }

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var bytes = memory.ToArray();

        var attachment = new NoteAttachment
        {
            NoteId = noteId,
            FileName = CleanName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim()[..Math.Min(150, file.ContentType.Trim().Length)],
            Size = bytes.Length,
        };
        db.NoteAttachments.Add(attachment);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        db.NoteAttachmentFiles.Add(new NoteAttachmentFile { AttachmentId = attachment.Id, Data = bytes, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return (new AttachmentView(attachment.Id, attachment.FileName, attachment.Size, attachment.CreateDate, IconFor(attachment.FileName)), null);
    }

    /// <summary>The name and the bytes of an attachment of this note, or <c>null</c>.</summary>
    public async Task<(string Name, byte[] Data)?> FileAsync(long noteId, long attachmentId)
    {
        var row = await db.NoteAttachmentFiles.AsNoTracking()
            .Where(f => f.AttachmentId == attachmentId && f.Attachment!.NoteId == noteId)
            .Select(f => new { f.Attachment!.FileName, f.Data }).FirstOrDefaultAsync();
        return row is null ? null : (row.FileName, row.Data);
    }

    public async Task<bool> DeleteAsync(long noteId, long attachmentId)
        => await db.NoteAttachments.Where(a => a.Id == attachmentId && a.NoteId == noteId).ExecuteDeleteAsync() > 0;

    /// <summary>The name of an uploaded file as it is kept: no path, no control characters, not longer than the column.</summary>
    public static string CleanName(string? name)
    {
        var clean = Path.GetFileName((name ?? string.Empty).Replace('\\', '/'));
        clean = new string(clean.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            clean = "file";
        }

        if (clean.Length > 260)
        {
            var ext = Path.GetExtension(clean);
            ext = ext.Length > 20 ? string.Empty : ext;
            clean = clean[..(260 - ext.Length)] + ext;
        }

        return clean;
    }

    public static string IconFor(string fileName) => NoteService.TypeFor(fileName).Type switch
    {
        NoteType.Docx => "file-text",
        NoteType.Xlsx => "table",
        NoteType.Pdf => "book",
        NoteType.File => Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".bmp" => "image",
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "archive",
            _ => "paperclip",
        },
        _ => "file",
    };
}
