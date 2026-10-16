using MatNote.Services;

namespace MatNote.Pages.Shared;

/// <summary>
/// For <c>_Attachments</c>: the files attached to a note. <paramref name="NoteId"/> is <c>null</c> for a note that is not saved yet (the section appears after the first save).
/// A visitor of a link (<paramref name="PublicToken"/>) can only download, and only when the link allows exports (<paramref name="CanDownload"/>).
/// </summary>
public sealed record AttachmentsModel(long? NoteId, IReadOnlyList<AttachmentView> Items, bool CanEdit, string? PublicToken = null, bool CanDownload = true)
{
    public string Url(long fileId) => PublicToken is null ? $"/Notes/Attachment?id={NoteId}&file={fileId}" : $"/s/{PublicToken}/attachment/{NoteId}/{fileId}";
}
