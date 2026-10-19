using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>A folder as the archive writer gets it: the folder to export and everything below it.</summary>
public sealed record ArchiveFolder(long Id, long? ParentId, string Name, string? Color);

public sealed class ImportFolder(string key, string? parentKey, string name, string? color)
{
    public string Key { get; } = key;

    public string? ParentKey { get; } = parentKey;

    public string Name { get; } = name;

    public string? Color { get; } = color;
}

public sealed class ImportNote
{
    /// <summary>The id the note had when the archive was made (<c>0</c> for a plain zip): what links in the archive point to.</summary>
    public long OldId { get; init; }

    public string? FolderKey { get; init; }

    public string Title { get; init; } = string.Empty;

    public NoteType Type { get; init; }

    /// <summary>The text of a text note.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>The bytes and the name of a file note.</summary>
    public byte[]? File { get; init; }

    public string? FileName { get; init; }

    public bool Favorite { get; init; }

    public List<string> Tags { get; } = [];

    public List<(string Name, byte[] Data)> Attachments { get; } = [];
}

/// <summary>What a zip holds, read into folders and notes, before anything is written to the database.</summary>
public sealed class ImportPlan
{
    /// <summary>The name of the archive: the folder that was exported, or the name of the zip.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The key of the folder that was exported (a MatNotes archive), or <c>null</c> for a plain zip.</summary>
    public string? RootKey { get; init; }

    public bool IsArchive => RootKey is not null;

    public List<ImportFolder> Folders { get; } = [];

    public List<ImportNote> Notes { get; } = [];

    /// <summary>What was left out and why (an empty file, a file that is too big, a name that is not allowed).</summary>
    public List<string> Skipped { get; } = [];
}

/// <summary>
/// A folder with everything inside as one zip that can be read anywhere (the notes as .html, .md, .txt, the files as they are, the attachments in a directory next to each note) and
/// that MatNotes can import again: <c>matnotes-folder.json</c> adds what a file name cannot say (type, tags, favorite, the id that links point to).
/// A plain zip of files and directories can be imported too (<see cref="Read"/>).
/// </summary>
public static class FolderArchive
{
    public const string ManifestName = "matnotes-folder.json";
    public const int MaxEntries = 20_000;
    public const long MaxTotalBytes = 1024L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed record ManifestFolder(long Id, long? ParentId, string Name, string? Color);

    private sealed record ManifestNote(long Id, long? FolderId, string Title, string Type, bool Favorite, List<string> Tags, string? Path, string? FileName, List<string> Attachments, DateTime Created, DateTime Updated);

    private sealed record Manifest(string App, string Format, int Version, string Name, long Root, List<ManifestFolder> Folders, List<ManifestNote> Notes);

    // ------------------------------------------------------------------------------------------------------------------------ writing

    public static async Task<ExportResult> WriteAsync(string name, long rootId, IReadOnlyList<ArchiveFolder> folders, IReadOnlyList<Note> notes, INoteFileStore store, long viewerId, CancellationToken ct = default)
    {
        var byId = folders.ToDictionary(f => f.Id);

        string DirOf(long? folderId)
        {
            var parts = new List<string>();
            long? at = folderId;
            while (at is { } current && current != rootId && byId.TryGetValue(current, out var f) && parts.Count < FolderService.MaxDepth + 2)
            {
                parts.Insert(0, f.Name);
                at = f.ParentId;
            }

            return string.Join('/', parts);
        }

        var manifestNotes = new List<ManifestNote>();
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>();
            foreach (var note in notes.OrderBy(n => n.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                var dir = DirOf(note.FolderId);
                byte[] data;
                string fileName;
                if (NoteDocs.IsText(note.Type))
                {
                    data = new UTF8Encoding(false).GetBytes(note.Content ?? string.Empty);
                    fileName = FileNames.Safe(note.Title) + note.Type switch { NoteType.RichText => ".html", NoteType.Markdown => ".md", _ => ".txt" };
                }
                else
                {
                    var bytes = await store.GetFileAsync(note.Id, ct);
                    if (bytes is null)
                    {
                        continue; // a file note without its bytes: nothing to keep
                    }

                    data = bytes;
                    fileName = string.IsNullOrWhiteSpace(note.FileName) ? note.Title : note.FileName;
                }

                var path = FileNames.UniquePath(dir, fileName, used);
                await AddAsync(zip, path, data, note, ct);

                var attachmentPaths = new List<string>();
                var attachmentDir = (dir.Length == 0 ? string.Empty : dir + "/") + FileNames.Safe(Path.GetFileNameWithoutExtension(path)) + " - attachments";
                foreach (var (attachmentName, attachmentData) in await store.GetAttachmentsAsync(note.Id, ct))
                {
                    var attachmentPath = FileNames.UniquePath(attachmentDir, attachmentName, used);
                    await AddAsync(zip, attachmentPath, attachmentData, note, ct);
                    attachmentPaths.Add(attachmentPath);
                }

                // the tags are the owner's: somebody who only reads a shared folder does not take them along
                var tags = note.OwnerId == viewerId ? (await store.GetTagNamesAsync(note.Id, ct)).ToList() : [];
                manifestNotes.Add(new ManifestNote(note.Id, note.FolderId, note.Title, note.Type.ToString(), note.IsFavorite, tags, path, NoteDocs.IsText(note.Type) ? null : fileName, attachmentPaths, note.CreateDate, note.UpdateDate));
            }

            var manifest = new Manifest("MatNotes", "folder", 1, name, rootId, folders.Select(f => new ManifestFolder(f.Id, f.ParentId, f.Name, f.Color)).ToList(), manifestNotes);
            var entry = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
            await using var target = entry.Open();
            await target.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(manifest, Json), ct);
        }

        return new ExportResult(stream.ToArray(), FileNames.WithExtension((string.IsNullOrWhiteSpace(name) ? "folder" : name) + ".matnotes", ".zip"), "application/zip");
    }

    private static async Task AddAsync(ZipArchive zip, string path, byte[] data, Note note, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(note.UpdateDate == default ? DateTime.UtcNow : note.UpdateDate, DateTimeKind.Utc));
        await using var target = entry.Open();
        await target.WriteAsync(data, ct);
    }

    // ------------------------------------------------------------------------------------------------------------------------ reading

    /// <summary>
    /// Reads a zip into folders and notes. With the manifest of MatNotes it restores what the manifest says (types, tags, favorites, which note is in which folder); without it
    /// every directory is a folder and every file a note (.txt/.log/.csv plain text, .md markdown, .html rich text, anything else as a file).
    /// An entry that is too big, empty or not allowed is skipped and named in <see cref="ImportPlan.Skipped"/>; a zip with too many entries or too much content is refused.
    /// </summary>
    /// <exception cref="InvalidDataException">Not a zip, too many entries, too much content, or a manifest that cannot be read.</exception>
    public static ImportPlan Read(Stream zipStream, long maxEntryBytes, string fallbackName)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException($"The zip has too many entries ({MaxEntries} at most).");
        }

        var budget = new Budget(MaxTotalBytes);
        var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName == ManifestName);
        return manifestEntry is null ? ReadPlain(zip, maxEntryBytes, fallbackName, budget) : ReadManifest(zip, manifestEntry, maxEntryBytes, budget);
    }

    private sealed class Budget(long left)
    {
        public long Left { get; private set; } = left;

        public void Spend(long bytes)
        {
            Left -= bytes;
            if (Left < 0)
            {
                throw new InvalidDataException("The zip holds too much (1 GB at most).");
            }
        }
    }

    /// <summary>The bytes of an entry, or <c>null</c> when it is bigger than <paramref name="max"/> (the real size counts, not the one the zip says).</summary>
    private static byte[]? ReadBounded(ZipArchiveEntry entry, long max, Budget budget)
    {
        if (entry.Length > max)
        {
            return null;
        }

        using var source = entry.Open();
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (memory.Length + read > max)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }

        budget.Spend(memory.Length);
        return memory.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static string Clamp(string text, int max) => text.Length <= max ? text : text[..max];

    private static ImportPlan ReadManifest(ZipArchive zip, ZipArchiveEntry manifestEntry, long maxEntryBytes, Budget budget)
    {
        var bytes = ReadBounded(manifestEntry, 20L * 1024 * 1024, budget) ?? throw new InvalidDataException("The list of the archive is too big.");
        Manifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<Manifest>(bytes, Json);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("The list of the archive (" + ManifestName + ") cannot be read.");
        }

        if (manifest is null || manifest.Version != 1 || manifest.Format != "folder" || manifest.Folders is null || manifest.Notes is null)
        {
            throw new InvalidDataException("This is not an archive of a folder that this version can import.");
        }

        var plan = new ImportPlan { Name = Clamp(string.IsNullOrWhiteSpace(manifest.Name) ? "Import" : manifest.Name, 120), RootKey = "f" + manifest.Root };
        var known = manifest.Folders.Select(f => f.Id).ToHashSet();
        foreach (var f in manifest.Folders)
        {
            var parent = f.ParentId is { } p && known.Contains(p) && f.Id != manifest.Root ? "f" + p : null;
            plan.Folders.Add(new ImportFolder("f" + f.Id, parent, Clamp(string.IsNullOrWhiteSpace(f.Name) ? "Folder" : f.Name, 120), Ui.IsColour(f.Color) ? f.Color : null));
        }

        if (known.Count == 0 || !known.Contains(manifest.Root))
        {
            throw new InvalidDataException("The archive does not say which folder it holds.");
        }

        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).GroupBy(e => e.FullName).ToDictionary(g => g.Key, g => g.First());
        foreach (var n in manifest.Notes)
        {
            var title = Clamp(string.IsNullOrWhiteSpace(n.Title) ? "Note" : n.Title.Trim(), NoteService.MaxTitleLength);
            if (n.Path is null || !entries.TryGetValue(n.Path, out var entry))
            {
                plan.Skipped.Add($"{title}: its file is not in the zip");
                continue;
            }

            var data = ReadBounded(entry, maxEntryBytes, budget);
            if (data is null)
            {
                plan.Skipped.Add($"{title}: too big");
                continue;
            }

            var type = Enum.TryParse<NoteType>(n.Type, out var parsed) && Enum.IsDefined(parsed) ? parsed : NoteType.File;
            var folderKey = n.FolderId is { } fid && known.Contains(fid) ? "f" + fid : plan.RootKey;
            var note = NoteDocs.IsText(type)
                ? new ImportNote { OldId = n.Id, FolderKey = folderKey, Title = title, Type = type, Content = Decode(data), Favorite = n.Favorite }
                : new ImportNote { OldId = n.Id, FolderKey = folderKey, Title = title, Type = NoteType.File, File = data, FileName = AttachmentService.CleanName(n.FileName ?? n.Path), Favorite = n.Favorite };
            note.Tags.AddRange((n.Tags ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => Clamp(t.Trim(), 60)).Take(30));
            foreach (var path in n.Attachments ?? [])
            {
                if (!entries.TryGetValue(path, out var attachmentEntry))
                {
                    plan.Skipped.Add($"{title}: an attachment is not in the zip");
                    continue;
                }

                var attachment = ReadBounded(attachmentEntry, maxEntryBytes, budget);
                if (attachment is null || attachment.Length == 0)
                {
                    plan.Skipped.Add($"{title}: the attachment {AttachmentService.CleanName(path)} is empty or too big");
                    continue;
                }

                note.Attachments.Add((AttachmentService.CleanName(path), attachment));
            }

            plan.Notes.Add(note);
        }

        return plan;
    }

    private static readonly string[] IgnoredNames = ["thumbs.db", "desktop.ini", ".ds_store"];

    private static ImportPlan ReadPlain(ZipArchive zip, long maxEntryBytes, string fallbackName, Budget budget)
    {
        var plan = new ImportPlan { Name = Clamp(string.IsNullOrWhiteSpace(fallbackName) ? "Import" : fallbackName, 120) };
        var keys = new HashSet<string>(StringComparer.Ordinal);

        string? FolderFor(string[] segments)
        {
            string? parent = null;
            var path = string.Empty;
            foreach (var segment in segments)
            {
                path = path.Length == 0 ? segment : path + "/" + segment;
                if (keys.Add(path))
                {
                    plan.Folders.Add(new ImportFolder(path, parent, Clamp(segment, 120), null));
                }

                parent = path;
            }

            return parent;
        }

        foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var parts = entry.FullName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0 || parts.Any(p => p is "." or ".." || p.StartsWith('.') || p == "__MACOSX" || p.Any(char.IsControl)))
            {
                if (!entry.FullName.EndsWith('/') && parts.Length > 0 && !parts[^1].StartsWith('.') && parts[0] != "__MACOSX")
                {
                    plan.Skipped.Add($"{entry.FullName}: the name is not allowed");
                }

                continue;
            }

            if (entry.FullName.EndsWith('/'))
            {
                FolderFor(parts); // an empty directory is a folder too
                continue;
            }

            var fileName = parts[^1];
            if (IgnoredNames.Contains(fileName.ToLowerInvariant()))
            {
                continue;
            }

            var data = ReadBounded(entry, maxEntryBytes, budget);
            if (data is null)
            {
                plan.Skipped.Add($"{entry.FullName}: too big");
                continue;
            }

            if (data.Length == 0)
            {
                plan.Skipped.Add($"{entry.FullName}: empty");
                continue;
            }

            var folderKey = FolderFor(parts[..^1]);
            var title = Clamp(Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } stem ? stem : fileName, NoteService.MaxTitleLength);
            var (type, textImport) = NoteService.TypeFor(fileName);
            plan.Notes.Add(textImport
                ? new ImportNote { FolderKey = folderKey, Title = title, Type = type, Content = Decode(data) }
                : new ImportNote { FolderKey = folderKey, Title = title, Type = NoteType.File, File = data, FileName = AttachmentService.CleanName(fileName) });
        }

        return plan;
    }

    // ------------------------------------------------------------------------------------------------------------------------ links

    /// <summary>
    /// The links to other notes in a text, pointed at the new ids of an import. <paramref name="map"/> gives the new id of an old one, or <c>null</c> for a note that is not part of the
    /// import: that link is removed (its text stays).
    /// </summary>
    public static string RemapLinks(NoteType type, string content, Func<long, long?> map)
    {
        content ??= string.Empty;
        if (type == NoteType.RichText)
        {
            if (!content.Contains("data-note-id", StringComparison.Ordinal))
            {
                return content;
            }

            var document = new HtmlParser().ParseDocument("<body>" + content);
            foreach (var a in document.QuerySelectorAll("a[data-note-id]").ToList())
            {
                if (long.TryParse(a.GetAttribute("data-note-id"), out var old) && map(old) is { } fresh)
                {
                    a.SetAttribute("data-note-id", fresh.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    a.SetAttribute("href", "/Notes/View?id=" + fresh.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    a.Replace([.. a.ChildNodes.ToList()]);
                }
            }

            return document.Body!.InnerHtml;
        }

        return MarkdownService.NoteToken().Replace(content, m => long.TryParse(m.Groups[2].Value, out var old) && map(old) is { } fresh
            ? $"[{m.Groups[1].Value}](note:{fresh})"
            : m.Groups[1].Value);
    }

    public static bool HasLinks(NoteType type, string content) => !string.IsNullOrEmpty(content) && (type == NoteType.RichText
        ? content.Contains("data-note-id", StringComparison.Ordinal)
        : content.Contains("(note:", StringComparison.Ordinal));
}
