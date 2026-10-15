namespace MatNote.Pages.Shared;

/// <summary>For <c>_PaneBar</c>: the note, whether the person may edit it, and which of the two modes is shown.</summary>
public sealed record PaneBarModel(long Id, string Title, bool CanEdit, bool Editing);
