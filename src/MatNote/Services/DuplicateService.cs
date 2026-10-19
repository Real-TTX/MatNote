using System.Text.RegularExpressions;
using MatNote.Data;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>
/// Duplicate a note (text or file, with its tags and attachments) or a folder with everything inside it. Only what the person owns: a note that was shared with them is not copied (it stays
/// where it is; they build their own notes from it with links). A copy is a new note with "(copy)" after the title; links in a copied folder that point inside it point at the copies,
/// links to other notes stay as they are. Shares are not copied.
/// </summary>
public sealed partial class DuplicateService(AppDbContext db, NoteService notes, AttachmentService attachments, INoteFileStore store, FolderService folders, FolderImporter importer)
{
    [GeneratedRegex(@"^(.*?) \(copy(?: (\d+))?\)$", RegexOptions.Singleline)]
    private static partial Regex CopySuffix();

    /// <summary>The title of a copy: "Plan" becomes "Plan (copy)", then "Plan (copy 2)", "Plan (copy 3)" ...; never longer than a title may be.</summary>
    public static string CopyTitle(string title, int max = NoteService.MaxTitleLength)
    {
        title = (title ?? string.Empty).Trim();
        var m = CopySuffix().Match(title);
        var stem = m.Success ? m.Groups[1].Value : title;
        var number = m.Success ? (m.Groups[2].Success ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + 1 : 2) : 1;
        var suffix = number == 1 ? " (copy)" : $" (copy {number})";
        if (stem.Length + suffix.Length > max)
        {
            stem = stem[..Math.Max(0, max - suffix.Length)].TrimEnd();
        }

        return stem + suffix;
    }

    /// <summary>A copy of a note of the person, in the same folder. <c>null</c> when it is not theirs.</summary>
    public async Task<Note?> DuplicateNoteAsync(long noteId, long ownerId)
    {
        var source = await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.OwnerId == ownerId);
        if (source is null)
        {
            return null;
        }

        var copy = new Note
        {
            OwnerId = ownerId,
            CreateUserId = ownerId,
            FolderId = source.FolderId,
            Title = CopyTitle(source.Title),
            Type = source.Type,
            Content = source.Content,
            IsFavorite = false,
        };

        IFormFile? file = null;
        if (!Ui.IsText(source.Type))
        {
            var bytes = await store.GetFileAsync(source.Id) ?? [];
            file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Upload", source.FileName ?? source.Title) { Headers = new HeaderDictionary(), ContentType = source.ContentType ?? "application/octet-stream" };
            copy.Type = NoteType.File;
        }

        var tags = await store.GetTagNamesAsync(source.Id);
        await notes.SaveAsync(copy, file, tags.Count == 0 ? null : string.Join(", ", tags));

        foreach (var (name, data) in await store.GetAttachmentsAsync(source.Id))
        {
            await attachments.AddAsync(copy.Id, new FormFile(new MemoryStream(data), 0, data.Length, "files", name) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" });
        }

        return copy;
    }

    /// <summary>A copy of a folder of the person, next to it, with its subfolders and notes (tags, attachments). The id of the new folder, or <c>null</c> when it is not theirs.</summary>
    public async Task<long?> DuplicateFolderAsync(long folderId, long ownerId)
    {
        var source = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == folderId && f.OwnerId == ownerId);
        if (source is null)
        {
            return null;
        }

        var refs = await folders.RefsAsync(ownerId);
        var ids = FolderTree.SelfAndDescendants(refs, source.Id);
        var tree = await db.Folders.AsNoTracking().Where(f => ids.Contains(f.Id)).Select(f => new ArchiveFolder(f.Id, f.ParentId, f.Name, f.Color)).ToListAsync();
        var inside = await db.Notes.AsNoTracking().Where(n => n.OwnerId == ownerId && n.FolderId != null && ids.Contains(n.FolderId.Value)).ToListAsync();

        // the same road as an export and an import, kept in memory: one code for "a folder as it is" and for "a folder again"
        var archive = await FolderArchive.WriteAsync(source.Name, source.Id, tree, inside, store, ownerId);
        var plan = FolderArchive.Read(new MemoryStream(archive.Data), long.MaxValue, source.Name);
        var result = await importer.ImportAsync(plan, ownerId, source.ParentId, new ImportOptions(CopyTitle(source.Name, 120), KeepOtherLinks: true));
        return result.OpenFolderId;
    }
}
