namespace MatNote.Pages.Shared;

/// <summary>
/// For <c>_PaneBar</c>: the note, whether the person may edit it, which of the two modes is shown, and whether the title is a field of the edit form (it is in the edit mode:
/// the title stays where it was). In the edit mode the bar also holds what the buttons under a form hold elsewhere: the state of the autosave, Save and Delete.
/// </summary>
public sealed record PaneBarModel(long Id, string Title, bool CanEdit, bool Editing, bool TitleField = false, bool Autosave = false, bool CanDelete = false);
