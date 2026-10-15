namespace MatNote.Pages.Shared;

/// <summary>
/// For <c>_ItemTabs</c>: the parts of one note or folder that have a page of their own are tabs (a note: <c>view</c>, <c>edit</c>, <c>share</c>; a folder: <c>folder</c>, <c>share</c>),
/// never buttons. <paramref name="Active"/> names the tab of the page. A tab the person may not use (edit, share) is left out.
/// </summary>
public sealed record ItemTabsModel(bool IsFolder, long Id, string Active, bool CanEdit = true, bool CanShare = true);
