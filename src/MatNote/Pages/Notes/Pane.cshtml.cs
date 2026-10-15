using Microsoft.AspNetCore.Mvc;

namespace MatNote.Pages.Notes;

/// <summary>What the note pane of the list shows when no note is picked (or after the shown note went to the trash).</summary>
public sealed class PaneModel : AppPageModel
{
    [BindProperty(SupportsGet = true)] public long? Folder { get; set; }

    [BindProperty(SupportsGet = true)] public bool Gone { get; set; }

    public void OnGet()
    {
    }
}
