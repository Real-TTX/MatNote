using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>The text of a version as lines, to compare two states of a note: a rich text note as the plain text of its blocks, markdown and plain text as they are.</summary>
public static class VersionText
{
    public static string Of(NoteType type, string content)
    {
        content ??= string.Empty;
        if (type != NoteType.RichText)
        {
            return content.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        }

        var note = new Note { Type = type, Content = content };
        return DocText.Render(new Doc(string.Empty, NoteDocs.Blocks(note), ShowTitle: false)).Replace("\r\n", "\n").TrimEnd('\n');
    }
}
