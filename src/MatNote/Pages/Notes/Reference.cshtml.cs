using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatNote.Pages.Notes;

/// <summary>
/// "Add to a note": puts a LINK to a note (one the person may only read, for example a snippet that was shared with them) at the end of one of their own text notes, or of a new one.
/// Nothing is copied: the note stays where it is and as it is, the page and the export show its current text where the link is. JSON back (no page of its own).
/// </summary>
public sealed class ReferenceModel(AccessService access, NoteService notes, CurrentUser me, INotePreview preview) : PageModel
{
    public async Task<IActionResult> OnPostAsync(long id, long? to, string? title)
    {
        var (source, _) = await access.FindNoteAsync(id);
        if (source is null)
        {
            return NotFound();
        }

        Note? target;
        var created = false;
        if (to is { } targetId)
        {
            // only a text note of the person: somebody else's note is never changed by a link
            var (found, level) = await access.FindNoteAsync(targetId, Access.Owner, track: true);
            if (found is null || level != Access.Owner || !Ui.IsText(found.Type))
            {
                return NotFound();
            }

            target = found;
        }
        else
        {
            var name = (title ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > NoteService.MaxTitleLength)
            {
                return BadRequest(new { ok = false, error = "Please give the new note a title (up to " + NoteService.MaxTitleLength + " characters)." });
            }

            target = new Note { OwnerId = me.RequiredId, Type = NoteType.RichText, Title = name, CreateUserId = me.RequiredId };
            created = true;
        }

        if (target.Id == source.Id)
        {
            return BadRequest(new { ok = false, error = "A note cannot link to itself." });
        }

        var already = !created && preview.ExtractLinks(target).Contains(source.Id);
        if (!already)
        {
            target.Content = Append(target.Type, target.Content, source);
            await notes.SaveAsync(target, null, null);
        }

        return new JsonResult(new { ok = true, id = target.Id, title = target.Title, already, created });
    }

    /// <summary>The text with one more link at the end, in the way of the type (a paragraph with a note link in rich text, <c>[Title](note:ID)</c> in plain text and markdown).</summary>
    public static string Append(NoteType type, string content, Note source)
    {
        content ??= string.Empty;
        if (type == NoteType.RichText)
        {
            return content + $"<p><a class=\"mn-note-link\" data-note-id=\"{source.Id}\" href=\"/Notes/View?id={source.Id}\">{Ui.HtmlEncode(source.Title)}</a></p>";
        }

        var title = source.Title.Replace('[', '(').Replace(']', ')').Replace('\r', ' ').Replace('\n', ' ');
        var gap = content.Length == 0 || content.EndsWith('\n') ? string.Empty : "\n";
        return content + gap + $"[{title}](note:{source.Id})\n";
    }
}
