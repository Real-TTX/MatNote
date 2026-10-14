using MatNote.Data;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services.Export;

/// <summary>What the exporter and the preview read from the database besides the note itself: the bytes of a file note and the names of the tags.</summary>
public interface INoteFileStore
{
    /// <summary>The bytes of the file of a file note, or <c>null</c> when there are none.</summary>
    Task<byte[]?> GetFileAsync(long noteId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetTagNamesAsync(long noteId, CancellationToken ct = default);
}

public sealed class DbNoteFileStore(AppDbContext db) : INoteFileStore
{
    public async Task<byte[]?> GetFileAsync(long noteId, CancellationToken ct = default) =>
        await db.NoteFiles.AsNoTracking().Where(f => f.NoteId == noteId).Select(f => f.Data).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> GetTagNamesAsync(long noteId, CancellationToken ct = default) =>
        await (from nt in db.NoteTags.AsNoTracking()
               join t in db.Tags.AsNoTracking() on nt.TagId equals t.Id
               where nt.NoteId == noteId
               orderby t.Name
               select t.Name).ToListAsync(ct);
}
