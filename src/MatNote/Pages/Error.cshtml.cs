using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages;

/// <summary>What a person sees when something goes wrong: a plain page without a menu. A note that does not exist and a note that is not yours look the same on purpose.</summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    [BindProperty(SupportsGet = true)] public int Code { get; set; } = 500;

    public string Heading => Code switch
    {
        404 => "Not found",
        403 => "No access",
        400 => "That did not work",
        _ => "Something went wrong",
    };

    public string Text => Code switch
    {
        404 => "This page does not exist, or you may not see it.",
        403 => "You may not open this page.",
        400 => "The request was not understood. Go back and try again.",
        _ => "An error happened on the server. Try again in a moment; if it stays, ask the administrator to look at the log.",
    };

    public void OnGet()
    {
    }

    public void OnPost()
    {
    }
}
