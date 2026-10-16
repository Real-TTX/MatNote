using MatNote.Data;
using MatNote.Pages.Notes;

namespace MatNote.Tests;

/// <summary>"Add to a note": the link that is put at the end of a note, in the way of the type of the note.</summary>
public class ReferenceTests
{
    private static Note Source(string title) => new() { Id = 7, Title = title, Type = NoteType.PlainText };

    [Fact]
    public void Rich_text_gets_a_paragraph_with_a_note_link()
    {
        var text = ReferenceModel.Append(NoteType.RichText, "<p>vor</p>", Source("A <b> & C"));
        Assert.Equal("<p>vor</p><p><a class=\"mn-note-link\" data-note-id=\"7\" href=\"/Notes/View?id=7\">A &lt;b&gt; &amp; C</a></p>", text);
    }

    [Fact]
    public void Markdown_and_plain_text_get_the_note_token_on_a_line_of_its_own()
    {
        Assert.Equal("[Snippet](note:7)\n", ReferenceModel.Append(NoteType.Markdown, "", Source("Snippet")));
        Assert.Equal("erste Zeile\n[Snippet](note:7)\n", ReferenceModel.Append(NoteType.PlainText, "erste Zeile", Source("Snippet")));
        Assert.Equal("erste Zeile\n[Snippet](note:7)\n", ReferenceModel.Append(NoteType.PlainText, "erste Zeile\n", Source("Snippet")));
    }

    [Fact]
    public void Brackets_and_line_breaks_in_a_title_cannot_break_the_token()
        => Assert.Equal("[a (b) c d](note:7)\n", ReferenceModel.Append(NoteType.Markdown, "", Source("a [b] c\nd")));
}
