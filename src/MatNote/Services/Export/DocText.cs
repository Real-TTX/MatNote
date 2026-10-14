using System.Text;

namespace MatNote.Services.Export;

/// <summary>The model as readable plain text: bullets, indented lists, tables as pipe separated lines, linked notes in ruled blocks.</summary>
internal static class DocText
{
    private const string RuleLine = "------------------------------------------------";

    public static string Render(Doc doc)
    {
        var lines = new List<string>();
        if (doc.NeedsTitleHeading)
        {
            var title = Clean(doc.Title.Trim());
            lines.Add(title);
            lines.Add(new string('=', Math.Clamp(title.Length, 3, 72)));
            lines.Add(string.Empty);
        }

        lines.AddRange(Blocks(doc.Blocks));
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }

    /// <summary>The text of inlines; a line break is a newline.</summary>
    public static string InlineText(IEnumerable<Inline> inlines, bool withLinks = true)
    {
        var sb = new StringBuilder();
        foreach (var i in inlines)
        {
            if (i.IsBreak)
            {
                sb.Append('\n');
                continue;
            }

            sb.Append(Clean(i.Text));
            if (withLinks && i.Href is { } href && i.NoteId is null && DocHtml.IsSafeHref(href) && href != i.Text && href.TrimEnd('/') != i.Text.TrimEnd('/'))
            {
                sb.Append(" (").Append(href).Append(')');
            }
        }

        return sb.ToString();
    }

    private static string Clean(string text) => text.Replace(' ', ' ').Replace("\r\n", "\n").Replace('\r', '\n');

    private static List<string> Blocks(List<Block> blocks, bool inItem = false)
    {
        var result = new List<string>();
        foreach (var block in blocks)
        {
            var lines = One(block);
            if (lines.Count == 0)
            {
                continue;
            }

            if (result.Count > 0 && !(inItem && block is ListBlock))
            {
                result.Add(string.Empty);
            }

            result.AddRange(lines);
        }

        return result;
    }

    private static List<string> One(Block block)
    {
        switch (block)
        {
            case Heading h:
            {
                var text = InlineText(h.Inlines).Replace('\n', ' ');
                var lines = new List<string> { text };
                if (h.Level <= 2)
                {
                    lines.Add(new string(h.Level == 1 ? '=' : '-', Math.Clamp(text.Length, 3, 72)));
                }

                return lines;
            }

            case Para p:
                return [.. InlineText(p.Inlines).Split('\n')];
            case CodeBlock c:
                return [.. c.Text.Replace("\r\n", "\n").Split('\n').Select(l => "    " + l)];
            case Rule:
                return [RuleLine];
            case Quote q:
                return [.. Blocks(q.Blocks).Select(l => l.Length == 0 ? ">" : "> " + l)];
            case ListBlock l:
                return ListLines(l);
            case TableBlock t:
                return [.. t.Rows.Select(r => string.Join(" | ", r.Select(c => InlineText(c.Inlines, false).Replace('\n', ' '))).TrimEnd())];
            case Section s:
            {
                var lines = new List<string> { $"===== {s.Title.Trim()} =====", string.Empty };
                lines.AddRange(Blocks(s.Blocks));
                if (lines[^1].Length != 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("=====");
                return lines;
            }

            case MdSource m:
                return [.. m.Text.Split('\n')];
            default:
                return [];
        }
    }

    private static List<string> ListLines(ListBlock list)
    {
        var result = new List<string>();
        var n = list.Start;
        foreach (var item in list.Items)
        {
            var marker = list.Ordered ? $"{n++}. " : "- ";
            var pad = new string(' ', marker.Length);
            var body = Blocks(item, true);
            if (body.Count == 0)
            {
                result.Add(marker.TrimEnd());
                continue;
            }

            for (var k = 0; k < body.Count; k++)
            {
                result.Add(k == 0 ? marker + body[k] : body[k].Length == 0 ? string.Empty : pad + body[k]);
            }
        }

        return result;
    }
}
