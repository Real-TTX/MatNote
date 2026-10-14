using System.Text.RegularExpressions;

namespace MatNote.Services.Export;

// A small document model: every note (rich text, markdown, plain text, or the text of a Word / Excel file) is read into it once, and every
// export format is written from it. Only the structure the editor can make is in it, so every writer is short and they all agree.

/// <summary>A piece of the document that stands on its own line(s).</summary>
internal abstract record Block;

internal sealed record Heading(int Level, List<Inline> Inlines) : Block;

/// <summary>A paragraph. In <see cref="LineMode"/> (plain text) the line breaks are significant: a linked note is put after the line that links it.</summary>
internal sealed record Para(List<Inline> Inlines, bool LineMode = false) : Block;

internal sealed record ListBlock(bool Ordered, List<List<Block>> Items, int Start = 1) : Block;

internal sealed record Quote(List<Block> Blocks) : Block;

internal sealed record CodeBlock(string Text) : Block;

internal sealed record Rule : Block;

internal sealed record Cell(bool Header, List<Inline> Inlines);

internal sealed record TableBlock(List<List<Cell>> Rows) : Block;

/// <summary>A linked note that is copied into the document: a titled section.</summary>
internal sealed record Section(string Title, List<Block> Blocks, int Depth) : Block
{
    /// <summary>The heading level the title gets (2 for the first level of links, at most 4).</summary>
    public int Level => Math.Clamp(Depth + 1, 2, 4);
}

/// <summary>A paragraph of markdown source, kept as it is (used when a markdown note is exported as markdown).</summary>
internal sealed record MdSource(string Text, bool IsCode) : Block;

/// <summary>A run of text with its formatting, or a line break. <see cref="NoteId"/> marks a link to another note.</summary>
internal sealed record Inline(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Underline = false,
    bool Strike = false,
    bool Code = false,
    string? Href = null,
    long? NoteId = null,
    bool IsBreak = false)
{
    public static readonly Inline Break = new(string.Empty, IsBreak: true);

    public bool SameFormat(Inline o) =>
        !IsBreak && !o.IsBreak && Bold == o.Bold && Italic == o.Italic && Underline == o.Underline && Strike == o.Strike && Code == o.Code && Href == o.Href && NoteId == o.NoteId;
}

internal sealed record Doc(string Title, List<Block> Blocks, bool ShowTitle = true)
{
    /// <summary>The title is not repeated when the content starts with the same heading.</summary>
    public bool NeedsTitleHeading
    {
        get
        {
            if (!ShowTitle || string.IsNullOrWhiteSpace(Title))
            {
                return false;
            }

            switch (Blocks.FirstOrDefault())
            {
                case Heading h:
                    return !string.Equals(Inlines.Plain(h.Inlines).Trim(), Title.Trim(), StringComparison.OrdinalIgnoreCase);
                case MdSource { IsCode: false } m:
                    var first = m.Text.Split('\n')[0].TrimStart('#', ' ').Trim();
                    return !(m.Text.StartsWith('#') && string.Equals(first, Title.Trim(), StringComparison.OrdinalIgnoreCase));
                default:
                    return true;
            }
        }
    }
}

internal static partial class Inlines
{
    public static string Plain(IEnumerable<Inline> inlines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var i in inlines)
        {
            sb.Append(i.IsBreak ? "\n" : i.Text);
        }

        return sb.ToString();
    }

    [GeneratedRegex("[ \\t\\r\\n\\f]+")]
    private static partial Regex Whitespace();

    /// <summary>Collapses white space like a browser does, trims the ends, drops empty runs and merges neighbours with the same format.</summary>
    public static List<Inline> Normalize(List<Inline> source, bool collapse = true)
    {
        var result = new List<Inline>();
        var atStart = true;
        foreach (var i0 in source)
        {
            if (i0.IsBreak)
            {
                TrimEnd(result);
                if (result.Count > 0)
                {
                    result.Add(Inline.Break);
                }

                atStart = true;
                continue;
            }

            var text = Strip(i0.Text);
            if (collapse)
            {
                text = Whitespace().Replace(text, " ");
                if (atStart && text.StartsWith(' '))
                {
                    text = text[1..];
                }
            }

            if (text.Length == 0)
            {
                continue;
            }

            atStart = collapse && text.EndsWith(' ');
            var i = i0 with { Text = text };
            if (result.Count > 0 && result[^1].SameFormat(i))
            {
                result[^1] = result[^1] with { Text = result[^1].Text + i.Text };
            }
            else
            {
                result.Add(i);
            }
        }

        if (collapse)
        {
            TrimEnd(result);
        }

        while (result.Count > 0 && result[^1].IsBreak)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static void TrimEnd(List<Inline> list)
    {
        while (list.Count > 0 && !list[^1].IsBreak)
        {
            var t = list[^1].Text.TrimEnd(' ');
            if (t.Length == 0)
            {
                list.RemoveAt(list.Count - 1);
                continue;
            }

            list[^1] = list[^1] with { Text = t };
            break;
        }
    }

    /// <summary>Removes the characters XML (Word, RTF readers) cannot carry: control characters and lone surrogates.</summary>
    public static string Strip(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var ok = true;
        for (var k = 0; k < text.Length && ok; k++)
        {
            ok = IsValid(text, k);
        }

        if (ok)
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        for (var k = 0; k < text.Length; k++)
        {
            if (IsValid(text, k))
            {
                sb.Append(text[k]);
            }
        }

        return sb.ToString();
    }

    private static bool IsValid(string s, int k)
    {
        var c = s[k];
        if (c < 0x20)
        {
            return c is '\t' or '\n' or '\r';
        }

        if (char.IsHighSurrogate(c))
        {
            return k + 1 < s.Length && char.IsLowSurrogate(s[k + 1]);
        }

        if (char.IsLowSurrogate(c))
        {
            return k > 0 && char.IsHighSurrogate(s[k - 1]);
        }

        return c is not ('￾' or '￿');
    }

    /// <summary>The inlines of a paragraph-like block, or an empty list.</summary>
    public static List<Inline> Of(Block block) => block switch
    {
        Para p => p.Inlines,
        Heading h => h.Inlines,
        _ => [],
    };
}
