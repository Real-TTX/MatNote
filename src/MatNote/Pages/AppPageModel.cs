using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages;

/// <summary>What the pages of the app have in common: the message after a save (a toast on the next page), the breadcrumb and a safe redirect.</summary>
public abstract class AppPageModel : PageModel
{
    protected void Flash(string message) => TempData["Flash"] = message;

    protected void FlashError(string message) => TempData["FlashError"] = message;

    /// <summary>The breadcrumb after the name of the app: the last entry is the page itself (it has no address).</summary>
    protected void Crumbs(params (string Text, string? Url)[] crumbs) => ViewData["Crumbs"] = crumbs;

    /// <summary>Goes back to where the person came from, but only inside this app (an address from outside is never followed).</summary>
    protected IActionResult RedirectLocal(string? url, string fallback = "/")
        => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? LocalRedirect(url) : LocalRedirect(fallback);

    /// <summary>The page is shown inside the note pane of the list (side by side layout): the layout has no menu, and addresses keep <c>embed=1</c>.</summary>
    public bool Embed => Request.Query.ContainsKey("embed") || (Request.HasFormContentType && Request.Form.ContainsKey("embed"));

    /// <summary>For a route value: <c>1</c> inside the pane, otherwise nothing (a null value is left out of the address).</summary>
    public int? EmbedValue => Embed ? 1 : null;

    /// <summary>The end of an address built by hand (<c>/Notes/View?id=5</c> + this).</summary>
    public string EmbedQuery => Embed ? "&embed=1" : string.Empty;

    public string? ErrorFor(string key) => ModelState.TryGetValue(key, out var entry) ? entry.Errors.FirstOrDefault()?.ErrorMessage : null;
}
