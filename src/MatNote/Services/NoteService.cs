using System.Security.Cryptography;
using System.Text;
using MatNote.Data;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>Saving, deleting and restoring notes: what has to happen around the text of a note (sanitizing, search text, tags, links, the file).</summary>
public sealed class NoteService(AppDbContext db, INotePreview preview, ConfigStore<AppConfig> config, NoteVersionService versions)
{
    public const int MaxTextLength = 5_000_000;
    public const int MaxTitleLength = 250;

    /// <summary>The type a file becomes, and whether its content is taken as the text of the note (a .txt, .md or .html file is imported as text, not kept as a file).</summary>
    public static (NoteType Type, bool IsTextImport) TypeFor(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".docx" => (NoteType.Docx, false),
            ".xlsx" => (NoteType.Xlsx, false),
            ".pdf" => (NoteType.Pdf, false),
            ".txt" or ".log" or ".csv" => (NoteType.PlainText, true),
            ".md" or ".markdown" => (NoteType.Markdown, true),
            ".html" or ".htm" => (NoteType.RichText, true),
            _ => (NoteType.File, false),
        };
    }

    public long MaxFileBytes => Math.Clamp(config.Value.MaxUploadMb, 1, 500) * 1024L * 1024L;

    /// <summary>The message for a file that is not allowed, or <c>null</c>.</summary>
    public string? CheckFile(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "The file is empty.";
        }

        return file.Length > MaxFileBytes ? $"The file is too big. The limit is {config.Value.MaxUploadMb} MB." : null;
    }

    /// <summary>
    /// Saves a note (new or changed). Rich text is sanitized, the text for the search is made, the bytes of a file go to their own table,
    /// the tags are set (only when <paramref name="tagNames"/> is not <c>null</c>: someone who may only edit does not change them) and the links to other notes are noted.
    /// </summary>
    public async Task SaveAsync(Note note, IFormFile? file, string? tagNames)
    {
        byte[]? bytes = null;
        if (file is not null)
        {
            await using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            bytes = memory.ToArray();
            var (type, textImport) = TypeFor(file.FileName);
            if (textImport)
            {
                note.Type = type;
                note.Content = Decode(bytes);
                note.FileName = null;
                note.ContentType = null;
                note.FileSize = 0;
                bytes = null;
            }
            else
            {
                note.Type = type;
                note.Content = string.Empty;
                note.FileName = Path.GetFileName(file.FileName);
                note.ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
                note.FileSize = bytes.Length;
            }
        }

        if (note.Type == NoteType.RichText)
        {
            note.Content = preview.Sanitize(note.Content);
        }

        var isNew = note.Id == 0;
        if (isNew)
        {
            db.Notes.Add(note);
        }

        if (bytes is not null || Ui.IsText(note.Type))
        {
            note.SearchText = await preview.BuildSearchTextAsync(note, bytes);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!isNew && Ui.IsText(note.Type))
        {
            // the state that this save replaces is kept when it is worth keeping (the history of a note, not every autosave)
            await versions.SnapshotBeforeSaveAsync(note);
        }

        await db.SaveChangesAsync();

        if (bytes is not null)
        {
            var row = isNew ? null : await db.NoteFiles.FirstOrDefaultAsync(f => f.NoteId == note.Id);
            if (row is null)
            {
                row = new NoteFile { NoteId = note.Id };
                db.NoteFiles.Add(row);
            }

            row.Data = bytes;
            row.Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        else if (Ui.IsText(note.Type) && !isNew)
        {
            // a file note that became a text note (a text file replaced it): the bytes are not needed any more
            await db.NoteFiles.Where(f => f.NoteId == note.Id).ExecuteDeleteAsync();
        }

        if (tagNames is not null)
        {
            await SetTagsAsync(note, tagNames);
        }

        await SyncLinksAsync(note);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }


    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    /// <summary>Sets the tags of a note from names separated by commas; a name that does not exist yet becomes a tag of the owner.</summary>
    public async Task SetTagsAsync(Note note, string tagNames)
    {
        var names = tagNames.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => n.Length > 60 ? n[..60] : n)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
        var existing = await db.Tags.Where(t => t.OwnerId == note.OwnerId).ToListAsync();
        var wanted = new List<Tag>();
        foreach (var name in names)
        {
            var tag = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                tag = new Tag { OwnerId = note.OwnerId, Name = name };
                db.Tags.Add(tag);
                existing.Add(tag);
            }

            wanted.Add(tag);
        }

        await db.SaveChangesAsync();
        var current = await db.NoteTags.Where(nt => nt.NoteId == note.Id).ToListAsync();
        foreach (var gone in current.Where(nt => wanted.All(t => t.Id != nt.TagId)))
        {
            db.NoteTags.Remove(gone);
        }

        foreach (var tag in wanted.Where(t => current.All(nt => nt.TagId != t.Id)))
        {
            db.NoteTags.Add(new NoteTag { NoteId = note.Id, TagId = tag.Id });
        }
    }

    /// <summary>Writes down which notes this note links to (for the backlinks); only notes that exist.</summary>
    public async Task SyncLinksAsync(Note note)
    {
        var wanted = preview.ExtractLinks(note).Where(id => id != note.Id).ToList();
        var existing = wanted.Count == 0 ? [] : await db.Notes.Where(n => wanted.Contains(n.Id)).Select(n => n.Id).ToListAsync();
        var current = await db.NoteLinks.Where(l => l.SourceNoteId == note.Id).ToListAsync();
        foreach (var gone in current.Where(l => !existing.Contains(l.TargetNoteId)))
        {
            db.NoteLinks.Remove(gone);
        }

        foreach (var id in existing.Where(id => current.All(l => l.TargetNoteId != id)))
        {
            db.NoteLinks.Add(new NoteLink { SourceNoteId = note.Id, TargetNoteId = id });
        }
    }

    public async Task<byte[]?> FileBytesAsync(long noteId)
        => await db.NoteFiles.AsNoTracking().Where(f => f.NoteId == noteId).Select(f => f.Data).FirstOrDefaultAsync();

    /// <summary>Moves a note to the trash; its shares end.</summary>
    public async Task DeleteAsync(Note note)
    {
        db.Notes.Remove(note);
        var shares = await db.Shares.Where(s => s.NoteId == note.Id).ToListAsync();
        foreach (var share in shares)
        {
            db.Shares.Remove(share);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Takes a note out of the trash. When its folder is gone too, it comes back into no folder.</summary>
    public async Task<bool> RestoreAsync(long noteId, long ownerId)
    {
        var note = await db.Notes.IgnoreQueryFilters().FirstOrDefaultAsync(n => n.Id == noteId && n.OwnerId == ownerId && n.UpdateState == SoftEntity.Deleted);
        if (note is null)
        {
            return false;
        }

        if (note.FolderId is { } folder && !await db.Folders.AnyAsync(f => f.Id == folder))
        {
            note.FolderId = null;
        }

        note.UpdateState = SoftEntity.Updated;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Removes a note from the trash for good.</summary>
    public async Task<bool> PurgeAsync(long noteId, long ownerId)
    {
        var count = await db.Notes.IgnoreQueryFilters().Where(n => n.Id == noteId && n.OwnerId == ownerId && n.UpdateState == SoftEntity.Deleted).ExecuteDeleteAsync();
        if (count > 0)
        {
            await db.NoteLinks.Where(l => l.SourceNoteId == noteId || l.TargetNoteId == noteId).ExecuteDeleteAsync();
        }

        return count > 0;
    }
}
