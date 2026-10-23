using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>A version as the history page lists it.</summary>
public sealed record VersionRow(long Id, DateTime StateDate, VersionKind Kind, string? Label, long? AuthorId, int Size, string Title);

/// <summary>When MatNotes keeps the state of a note that a save is about to replace. Pure, so that it can be tested.</summary>
public static class VersionPolicy
{
    /// <summary>A snapshot is not made more often than this (except the very first one).</summary>
    public static readonly TimeSpan MinSpacing = TimeSpan.FromMinutes(1);

    /// <summary>A pause this long ends a sitting: the next change starts a new one.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(5);

    /// <summary>A sitting that goes on gets a snapshot this often.</summary>
    public static readonly TimeSpan LongSitting = TimeSpan.FromMinutes(30);

    public const int MaxAuto = 80;
    public const int MaxManual = 50;

    /// <summary>
    /// A save replaces a state (saved at <paramref name="stateDate"/> by <paramref name="stateAuthor"/>). It is kept when it is the first one, when somebody else is about to replace it,
    /// when it is the work of an earlier sitting (a pause of five minutes before this change) or when the sitting has gone on for half an hour since the last snapshot.
    /// An autosave while somebody types does none of that: it only continues the sitting.
    /// </summary>
    public static bool IsDue(DateTime? lastSnapshot, DateTime stateDate, long? stateAuthor, long editor, DateTime now)
    {
        if (lastSnapshot is not { } last)
        {
            return true;
        }

        if (now - last < MinSpacing)
        {
            return false;
        }

        return stateAuthor != editor || now - stateDate >= Pause || now - last >= LongSitting;
    }
}

/// <summary>The history of a text note: versions that are kept when it makes sense (not at every autosave), listed, compared, saved on purpose and restored.</summary>
public sealed class NoteVersionService(AppDbContext db, CurrentUser me)
{
    public async Task<List<VersionRow>> ListAsync(long noteId)
        => await db.NoteVersions.AsNoTracking().Where(v => v.NoteId == noteId).OrderByDescending(v => v.StateDate).ThenByDescending(v => v.Id)
            .Select(v => new VersionRow(v.Id, v.StateDate, v.Kind, v.Label, v.AuthorId, v.Size, v.Title)).ToListAsync();

    public async Task<NoteVersion?> GetAsync(long noteId, long versionId)
        => await db.NoteVersions.AsNoTracking().FirstOrDefaultAsync(v => v.NoteId == noteId && v.Id == versionId);

    /// <summary>
    /// Called by a save of a text note that is already in the database, before the new text is written: keeps the state that is replaced when the policy says so. The note must be tracked
    /// (its original values are the state). The snapshot is added to the context and saved together with the note.
    /// </summary>
    public async Task SnapshotBeforeSaveAsync(Note note)
    {
        db.ChangeTracker.DetectChanges();
        var entry = db.Entry(note);
        if (entry.State != EntityState.Modified)
        {
            return;
        }

        var title = (string)entry.Property(n => n.Title).OriginalValue!;
        var content = (string)entry.Property(n => n.Content).OriginalValue!;
        if (title == note.Title && content == note.Content)
        {
            return; // only tags or the folder changed: the text is the same
        }

        if (content.Length == 0 && title.Length == 0)
        {
            return;
        }

        var editor = me.Id;
        if (editor is null)
        {
            return;
        }

        var stateDate = (DateTime)entry.Property(n => n.UpdateDate).OriginalValue!;
        var author = (long?)entry.Property(n => n.UpdateUserId).OriginalValue ?? note.OwnerId;
        var last = await db.NoteVersions.AsNoTracking().Where(v => v.NoteId == note.Id).OrderByDescending(v => v.CreateDate).Select(v => new { v.CreateDate, v.Title, v.Content }).FirstOrDefaultAsync();
        if (last is not null && last.Title == title && last.Content == content)
        {
            return; // this state is the last snapshot already (a restore made it)
        }

        if (!VersionPolicy.IsDue(last?.CreateDate, stateDate, author, editor.Value, DateTime.UtcNow))
        {
            return;
        }

        db.NoteVersions.Add(new NoteVersion { NoteId = note.Id, Title = title, Content = content, Kind = VersionKind.Auto, AuthorId = author, StateDate = stateDate, Size = content.Length });
        await PruneAsync(note.Id);
    }

    /// <summary>Saves the state a note has now as a version on purpose, with an optional name. The note is not changed.</summary>
    public async Task<NoteVersion> CheckpointAsync(Note note, string? label)
    {
        label = string.IsNullOrWhiteSpace(label) ? null : label.Trim()[..Math.Min(120, label.Trim().Length)];
        var version = new NoteVersion
        {
            NoteId = note.Id,
            Title = note.Title,
            Content = note.Content,
            Kind = VersionKind.Manual,
            Label = label,
            AuthorId = note.UpdateUserId ?? note.OwnerId,
            StateDate = note.UpdateDate,
            Size = note.Content.Length,
        };
        db.NoteVersions.Add(version);
        await db.SaveChangesAsync();
        await PruneAsync(note.Id);
        await db.SaveChangesAsync();
        return version;
    }

    /// <summary>
    /// Puts an older version back as the text of the note. What the note has now is kept first (a version "before a restore"), so a restore can be undone. The caller saves the note (the text goes
    /// through the usual save: sanitized, search text, links).
    /// </summary>
    public async Task<bool> PrepareRestoreAsync(Note note, NoteVersion version)
    {
        if (version.Title == note.Title && version.Content == note.Content)
        {
            return false; // nothing to restore: it is the text of the note already
        }

        db.NoteVersions.Add(new NoteVersion
        {
            NoteId = note.Id,
            Title = note.Title,
            Content = note.Content,
            Kind = VersionKind.BeforeRestore,
            AuthorId = note.UpdateUserId ?? note.OwnerId,
            StateDate = note.UpdateDate,
            Size = note.Content.Length,
        });
        await db.SaveChangesAsync(); // the old state is kept before the note changes: the usual save then finds it as the last snapshot
        note.Title = version.Title;
        note.Content = version.Content;
        return true;
    }

    /// <summary>Keeps the history from growing without end: the oldest automatic versions go first, then the oldest of the ones somebody saved.</summary>
    private async Task PruneAsync(long noteId)
    {
        await db.SaveChangesAsync();
        async Task Trim(Func<VersionKind, bool> match, int keep)
        {
            var ids = await db.NoteVersions.Where(v => v.NoteId == noteId).OrderByDescending(v => v.StateDate).ThenByDescending(v => v.Id).Select(v => new { v.Id, v.Kind }).ToListAsync();
            var surplus = ids.Where(v => match(v.Kind)).Skip(keep).Select(v => v.Id).ToList();
            if (surplus.Count > 0)
            {
                await db.NoteVersions.Where(v => surplus.Contains(v.Id)).ExecuteDeleteAsync();
            }
        }

        await Trim(kind => kind != VersionKind.Manual, VersionPolicy.MaxAuto);
        await Trim(kind => kind == VersionKind.Manual, VersionPolicy.MaxManual);
    }
}
