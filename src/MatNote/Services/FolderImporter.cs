using MatNote.Data;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Http;

namespace MatNote.Services;

/// <summary>How an import differs from the usual one: RootName renames the folder that was exported, KeepOtherLinks keeps links to notes that are not part of the import (a copy inside the same app: they still point at the originals).</summary>
public sealed record ImportOptions(string? RootName = null, bool KeepOtherLinks = false);

/// <summary>What an import made.</summary>
public sealed record ImportResult(int Folders, int Notes, int Attachments, long? OpenFolderId, IReadOnlyList<string> Skipped);

/// <summary>
/// Writes an <see cref="ImportPlan"/> into the folders and notes of ONE person: always their own, in a folder of theirs (or at the top). The notes are saved like any other note
/// (sanitized, searchable, tags, links), so an import can never bring anything in that a note cannot hold. Links inside an archive are pointed at the new notes in a second step.
/// </summary>
public sealed class FolderImporter(AppDbContext db, NoteService notes, AttachmentService attachments, FolderService folders)
{
    public async Task<ImportResult> ImportAsync(ImportPlan plan, long ownerId, long? targetId, ImportOptions? options = null)
    {
        options ??= new ImportOptions();
        var inside = plan.Notes.Where(n => n.OldId > 0).Select(n => n.OldId).ToHashSet();
        var skipped = new List<string>(plan.Skipped);
        var baseDepth = targetId is { } t ? await folders.DepthAsync(t, ownerId) : 0;

        // a plain zip with several things at its top (or loose files) goes into a folder of its own, named like the zip
        var tops = plan.Folders.Where(f => f.ParentKey is null).ToList();
        var wrap = !plan.IsArchive && !(tops.Count == 1 && plan.Notes.All(n => n.FolderKey is not null));
        var folderIds = new Dictionary<string, long>();
        var created = 0;
        long? openId = targetId;
        long? wrapperId = null;

        if (wrap)
        {
            var wrapper = new Folder { OwnerId = ownerId, ParentId = targetId, Name = plan.Name };
            db.Folders.Add(wrapper);
            await db.SaveChangesAsync();
            wrapperId = wrapper.Id;
            openId = wrapper.Id;
            created++;
            baseDepth++;
        }

        // the folders, level by level (a child needs the id of its parent)
        var children = plan.Folders.ToLookup(f => f.ParentKey ?? string.Empty);
        var level = new List<(ImportFolder Folder, long? ParentId, int Depth)>(tops.Select(f => (f, wrapperId ?? targetId, baseDepth + 1)));
        while (level.Count > 0)
        {
            var next = new List<(ImportFolder, long?, int)>();
            var pending = new List<(ImportFolder Folder, Folder Row, int Depth)>();
            foreach (var (folder, parentId, depth) in level)
            {
                if (folderIds.ContainsKey(folder.Key))
                {
                    continue;
                }

                if (depth > FolderService.MaxDepth)
                {
                    // too deep: what is in it goes into the folder above
                    if (parentId is { } up)
                    {
                        folderIds[folder.Key] = up;
                        skipped.Add($"{folder.Name}: nested too deep, its content is in the folder above");
                    }

                    next.AddRange(children[folder.Key].Select(c => (c, parentId, depth + 1)));
                    continue;
                }

                var row = new Folder { OwnerId = ownerId, ParentId = parentId, Name = options.RootName is { Length: > 0 } rename && folder.Key == plan.RootKey ? rename : folder.Name, Color = folder.Color };
                db.Folders.Add(row);
                pending.Add((folder, row, depth));
            }

            await db.SaveChangesAsync();
            foreach (var (folder, row, depth) in pending)
            {
                folderIds[folder.Key] = row.Id;
                created++;
                next.AddRange(children[folder.Key].Select(c => (c, (long?)row.Id, depth + 1)));
            }

            level = next;
        }

        if (!wrap && tops.Count > 0 && folderIds.TryGetValue(plan.RootKey ?? tops[0].Key, out var rootId))
        {
            openId = rootId;
        }

        // the notes
        var ids = new Dictionary<long, long>();
        var withLinks = new List<(Note Note, ImportNote Source)>();
        var noteCount = 0;
        var attachmentCount = 0;
        foreach (var source in plan.Notes)
        {
            long? folderId = source.FolderKey is { } key && folderIds.TryGetValue(key, out var fid) ? fid : wrapperId ?? targetId;
            var hasLinks = plan.IsArchive && Ui.IsText(source.Type) && FolderArchive.HasLinks(source.Type, source.Content);
            var note = new Note
            {
                OwnerId = ownerId,
                CreateUserId = ownerId,
                FolderId = folderId,
                Title = source.Title,
                Type = source.Type,
                IsFavorite = source.Favorite,
                // the links point at ids of the other computer: they are set again when every note has its new id
                Content = hasLinks ? FolderArchive.RemapLinks(source.Type, source.Content, old => options.KeepOtherLinks && !inside.Contains(old) ? old : null) : source.Content,
            };

            try
            {
                await notes.SaveAsync(note, source.File is null ? null : FileOf(source.File, source.FileName ?? source.Title), source.Tags.Count == 0 ? null : string.Join(", ", source.Tags));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.Entry(note).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                skipped.Add($"{source.Title}: could not be saved");
                continue;
            }

            noteCount++;
            if (source.OldId > 0)
            {
                ids[source.OldId] = note.Id;
            }

            if (hasLinks)
            {
                withLinks.Add((note, source));
            }

            foreach (var (name, data) in source.Attachments)
            {
                var (added, error) = await attachments.AddAsync(note.Id, FileOf(data, name));
                if (added is null)
                {
                    skipped.Add($"{source.Title}: {error}");
                }
                else
                {
                    attachmentCount++;
                }
            }
        }

        foreach (var (note, source) in withLinks)
        {
            note.Content = FolderArchive.RemapLinks(source.Type, source.Content, old => ids.TryGetValue(old, out var fresh) ? fresh : options.KeepOtherLinks ? old : null);
            await notes.SaveAsync(note, null, null);
        }

        return new ImportResult(created, noteCount, attachmentCount, openId, skipped);
    }

    private static FormFile FileOf(byte[] data, string name)
        => new(new MemoryStream(data), 0, data.Length, "Upload", AttachmentService.CleanName(name)) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };
}
