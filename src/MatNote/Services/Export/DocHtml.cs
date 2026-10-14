using System.Net;
using System.Text;

namespace MatNote.Services.Export;

/// <summary>The model as HTML. Everything is encoded and only links with a safe scheme are written, so the result needs no further cleaning.</summary>
internal static class DocHtml
{
    private const string Style =
        "body{font-family:Calibri,'Segoe UI',Arial,sans-serif;line-height:1.55;max-width:50em;margin:2em auto;padding:0 1em;color:#222}" +
        "h1,h2,h3,h4{line-height:1.25}pre{background:#f4f4f4;padding:.8em;overflow:auto}code{font-family:Consolas,'Courier New',monospace}" +
        "blockquote{border-left:3px solid #ccc;margin-left:0;padding-left:1em;color:#555}table{border-collapse:collapse}" +
        "td,th{border:1px solid #bbb;padding:.3em .6em;vertical-align:top}th{background:#f0f0f0}" +
        "section.mn-linked-note{border-left:4px solid #e8a33d;margin:1.2em 0;padding:.2em 0 .2em 1em;background:#fdf8ef}";

    public static bool IsSafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || href.Any(char.IsControl))
        {
            return false;
        }

        return Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto";
    }

    public static string Document(Doc doc)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>")
            .Append(Enc(doc.Title)).Append("</title>\n<style>").Append(Style).Append("</style>\n</head>\n<body>\n");
        if (doc.NeedsTitleHeading)
        {
            sb.Append("<h1>").Append(Enc(doc.Title.Trim())).Append("</h1>\n");
        }

        Blocks(doc.Blocks, sb);
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    public static string Fragment(List<Block> blocks)
    {
        var sb = new StringBuilder();
        Blocks(blocks, sb);
        return sb.ToString();
    }

    /// <summary>Encodes the five characters that matter in HTML text and attributes; everything else stays as it is (the page is UTF-8).</summary>
    internal static string Enc(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    private static void Blocks(List<Block> blocks, StringBuilder sb)
    {
        foreach (var b in blocks)
        {
            One(b, sb);
        }
    }

    private static void One(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case Heading h:
                var n = Math.Clamp(h.Level, 1, 6);
                sb.Append("<h").Append(n).Append('>');
                Inline(h.Inlines, sb);
                sb.Append("</h").Append(n).Append(">\n");
                break;
            case Para p:
                sb.Append("<p>");
                Inline(p.Inlines, sb);
                sb.Append("</p>\n");
                break;
            case CodeBlock c:
                sb.Append("<pre><code>").Append(Enc(c.Text)).Append("</code></pre>\n");
                break;
            case Rule:
                sb.Append("<hr>\n");
                break;
            case Quote q:
                sb.Append("<blockquote>\n");
                Blocks(q.Blocks, sb);
                sb.Append("</blockquote>\n");
                break;
            case ListBlock l:
                var tag = l.Ordered ? "ol" : "ul";
                sb.Append('<').Append(tag);
                if (l.Ordered && l.Start != 1)
                {
                    sb.Append(" start=\"").Append(l.Start).Append('"');
                }

                sb.Append(">\n");
                foreach (var item in l.Items)
                {
                    sb.Append("<li>");
                    if (item.Count == 1 && item[0] is Para only)
                    {
                        Inline(only.Inlines, sb);
                    }
                    else
                    {
                        sb.Append('\n');
                        Blocks(item, sb);
                    }

                    sb.Append("</li>\n");
                }

                sb.Append("</").Append(tag).Append(">\n");
                break;
            case TableBlock t:
                sb.Append("<table>\n");
                foreach (var row in t.Rows)
                {
                    sb.Append("<tr>");
                    foreach (var cell in row)
                    {
                        var ct = cell.Header ? "th" : "td";
                        sb.Append('<').Append(ct).Append('>');
                        Inline(cell.Inlines, sb);
                        sb.Append("</").Append(ct).Append('>');
                    }

                    sb.Append("</tr>\n");
                }

                sb.Append("</table>\n");
                break;
            case Section s:
                var level = s.Level;
                sb.Append("<section class=\"mn-linked-note\">\n<h").Append(level).Append('>').Append(Enc(s.Title.Trim())).Append("</h").Append(level).Append(">\n");
                Blocks(s.Blocks, sb);
                sb.Append("</section>\n");
                break;
            case MdSource m:
                sb.Append("<pre>").Append(Enc(m.Text)).Append("</pre>\n");
                break;
        }
    }

    private static void Inline(List<Inline> inlines, StringBuilder sb)
    {
        foreach (var i in inlines)
        {
            if (i.IsBreak)
            {
                sb.Append("<br>");
                continue;
            }

            var open = new StringBuilder();
            var close = new StringBuilder();
            if (i.Href is { } href && i.NoteId is null && IsSafeHref(href))
            {
                open.Append("<a href=\"").Append(Enc(href.Trim())).Append("\" rel=\"noopener noreferrer\">");
                close.Insert(0, "</a>");
            }

            void Wrap(bool on, string t)
            {
                if (on)
                {
                    open.Append('<').Append(t).Append('>');
                    close.Insert(0, "</" + t + ">");
                }
            }

            Wrap(i.Code, "code");
            Wrap(i.Bold, "strong");
            Wrap(i.Italic, "em");
            Wrap(i.Underline, "u");
            Wrap(i.Strike, "s");
            sb.Append(open).Append(Enc(i.Text)).Append(close);
        }
    }
}
