using System.Text;
using System.Text.RegularExpressions;

namespace MatNote.Services.Export;

/// <summary>The model as markdown (CommonMark with pipe tables and strike-through; underline stays HTML because markdown has none).</summary>
internal static partial class DocMarkdown
{
    [GeneratedRegex("^(\\s*)(?:(#{1,6}|[-+])(?=\\s|$)|(\\d+)([.)])(?=\\s)|(={3,}|-{3,})\\s*$)")]
    private static partial Regex BlockStart();

    public static string Render(Doc doc)
    {
        var lines = new List<string>();
        if (doc.NeedsTitleHeading)
        {
            lines.Add("# " + Escape(doc.Title.Trim()).Replace('\n', ' '));
            lines.Add(string.Empty);
        }

        lines.AddRange(Blocks(doc.Blocks));
        return string.Join("\n", lines).TrimEnd('\n') + "\n";
    }

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
                return [new string('#', Math.Clamp(h.Level, 1, 6)) + " " + InlineMd(h.Inlines).Replace("  \n", " ").Replace('\n', ' ')];
            case Para p:
                return [.. InlineMd(p.Inlines).Split('\n').Select(EscapeLineStart)];
            case CodeBlock c:
            {
                var fence = new string('`', Math.Max(3, LongestRun(c.Text, '`') + 1));
                return [fence, .. c.Text.Replace("\r\n", "\n").Split('\n'), fence];
            }

            case Rule:
                return ["---"];
            case Quote q:
                return [.. Blocks(q.Blocks).Select(l => l.Length == 0 ? ">" : "> " + l)];
            case ListBlock l:
                return ListLines(l);
            case TableBlock t:
                return Table(t);
            case Section s:
            {
                var lines = new List<string> { "---", string.Empty, new string('#', s.Level) + " " + Escape(s.Title.Trim()).Replace('\n', ' '), string.Empty };
                lines.AddRange(Blocks(s.Blocks));
                lines.Add(string.Empty);
                lines.Add("---");
                return lines;
            }

            case MdSource m:
                return [.. m.Text.Replace("\r\n", "\n").Split('\n')];
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

    private static List<string> Table(TableBlock table)
    {
        var lines = new List<string>();
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var cells = table.Rows[r].Select(c => InlineMd(c.Inlines, true).Replace("  \n", "<br>").Replace("\n", "<br>").Replace("|", "\\|"));
            lines.Add("| " + string.Join(" | ", cells) + " |");
            if (r == 0)
            {
                lines.Add("|" + string.Concat(Enumerable.Repeat(" --- |", table.Rows[0].Count)));
            }
        }

        return lines;
    }

    private static string EscapeLineStart(string line) => BlockStart().Replace(
        line,
        m => m.Groups[3].Success
            ? m.Groups[1].Value + m.Groups[3].Value + "\\" + m.Groups[4].Value
            : m.Groups[1].Value + "\\" + (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[5].Value));

    internal static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '~')
            {
                sb.Append('\\');
            }

            sb.Append(c == ' ' ? ' ' : c);
        }

        return sb.ToString();
    }

    private static int LongestRun(string text, char c)
    {
        int best = 0, run = 0;
        foreach (var ch in text)
        {
            run = ch == c ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return best;
    }

    private static string InlineMd(List<Inline> inlines, bool inTable = false)
    {
        var sb = new StringBuilder();
        var k = 0;
        while (k < inlines.Count)
        {
            var i = inlines[k];
            if (i.IsBreak)
            {
                sb.Append("  \n");
                k++;
                continue;
            }

            if (i.Href is { } href && i.NoteId is null && DocHtml.IsSafeHref(href))
            {
                var inner = new StringBuilder();
                while (k < inlines.Count && !inlines[k].IsBreak && inlines[k].Href == href && inlines[k].NoteId is null)
                {
                    inner.Append(Run(inlines[k] with { Href = null }));
                    k++;
                }

                var text = inner.ToString();
                sb.Append('[').Append(text.Length == 0 ? Escape(href) : text).Append("](").Append(EncodeHref(href)).Append(')');
                continue;
            }

            sb.Append(Run(i));
            k++;
        }

        return sb.ToString();
    }

    private static string EncodeHref(string href) => href.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29").Replace("<", "%3C").Replace(">", "%3E");

    private static string Run(Inline i)
    {
        var text = i.Text.Replace("\r\n", "\n").Replace('\n', ' ');
        var core = text.Trim();
        if (core.Length == 0)
        {
            return text.Length == 0 ? string.Empty : " ";
        }

        var lead = text[..(text.Length - text.TrimStart().Length)];
        var trail = text[text.TrimEnd().Length..];
        string body;
        if (i.Code)
        {
            var ticks = new string('`', LongestRun(core, '`') + 1);
            var pad = core.StartsWith('`') || core.EndsWith('`') ? " " : string.Empty;
            body = ticks + pad + core + pad + ticks;
        }
        else
        {
            body = Escape(core);
        }

        if (i.Bold)
        {
            body = "**" + body + "**";
        }

        if (i.Italic)
        {
            body = "*" + body + "*";
        }

        if (i.Strike)
        {
            body = "~~" + body + "~~";
        }

        if (i.Underline)
        {
            body = "<u>" + body + "</u>";
        }

        return (lead.Length > 0 ? " " : string.Empty) + body + (trail.Length > 0 ? " " : string.Empty);
    }
}
