using System.Text;

namespace MatNote.Services.Export;

/// <summary>The model as RTF 1.x: Calibri / Courier New, headings, bullets and numbers as indented paragraphs, tables, hyperlink fields. Non-ASCII goes out as <c>\uN?</c>.</summary>
internal static class DocRtf
{
    private const int PageWidth = 9000; // twips of text width (A4 with 2.5 cm margins is about 9070)

    public static string Render(Doc doc)
    {
        var sb = new StringBuilder();
        sb.Append("{\\rtf1\\ansi\\ansicpg1252\\uc1\\deff0\\deflang1033");
        sb.Append("{\\fonttbl{\\f0\\fswiss\\fcharset0 Calibri;}{\\f1\\fmodern\\fcharset0 Courier New;}}");
        sb.Append("{\\colortbl;\\red5\\green99\\blue193;\\red128\\green128\\blue128;\\red89\\green89\\blue89;}");
        sb.Append("{\\info{\\title ").Append(Escape(doc.Title)).Append("}}");
        sb.Append("\\viewkind4\\paperw11906\\paperh16838\\margl1417\\margr1417\\margt1417\\margb1417\\f0\\fs22\n");
        if (doc.NeedsTitleHeading)
        {
            sb.Append("\\pard\\sa240\\b\\fs44 ").Append(Escape(doc.Title.Trim())).Append("\\b0\\fs22\\par\n");
        }

        Blocks(doc.Blocks, sb, 0);
        sb.Append("\\pard\\par\n}");
        return sb.ToString();
    }

    /// <summary>Escapes text for RTF: backslash, braces, and everything outside ASCII as a signed UTF-16 unit with a fallback character.</summary>
    internal static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '{':
                    sb.Append("\\{");
                    break;
                case '}':
                    sb.Append("\\}");
                    break;
                case '\t':
                    sb.Append("\\tab ");
                    break;
                case '\n':
                    sb.Append("\\line ");
                    break;
                case '\r':
                    break;
                case < ' ':
                    break;
                case > '~':
                    sb.Append("\\u").Append((short)c).Append('?');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static void Blocks(List<Block> blocks, StringBuilder sb, int indent)
    {
        foreach (var b in blocks)
        {
            One(b, sb, indent);
        }
    }

    private static string Indent(int twips) => twips > 0 ? "\\li" + twips : string.Empty;

    private static void One(Block block, StringBuilder sb, int indent)
    {
        switch (block)
        {
            case Heading h:
            {
                var size = h.Level switch { 1 => 36, 2 => 30, 3 => 26, _ => 24 };
                sb.Append("\\pard\\keepn").Append(Indent(indent)).Append("\\sb240\\sa120\\b\\fs").Append(size).Append(' ');
                Inline(h.Inlines, sb);
                sb.Append("\\b0\\fs22\\par\n");
                break;
            }

            case Para p:
                sb.Append("\\pard").Append(Indent(indent)).Append("\\sa160 ");
                Inline(p.Inlines, sb);
                sb.Append("\\par\n");
                break;
            case CodeBlock c:
                foreach (var line in c.Text.Replace("\r\n", "\n").Split('\n'))
                {
                    sb.Append("\\pard").Append(Indent(indent + 120)).Append("\\sa0\\f1\\fs20 ").Append(Escape(line)).Append("\\f0\\fs22\\par\n");
                }

                sb.Append("\\pard\\sa160\\par\n");
                break;
            case Rule:
                sb.Append("\\pard").Append(Indent(indent)).Append("\\brdrb\\brdrs\\brdrw10\\brsp20\\brdrcf2\\sa160\\par\n");
                break;
            case Quote q:
                QuoteBlocks(q.Blocks, sb, indent + 567);
                break;
            case ListBlock l:
                ListItems(l, sb, indent);
                break;
            case TableBlock t:
                Table(t, sb);
                break;
            case Section s:
                One(new Heading(s.Level, [new Inline(s.Title.Trim())]), sb, indent);
                Blocks(s.Blocks, sb, indent);
                sb.Append("\\pard\\brdrb\\brdrs\\brdrw10\\brsp20\\brdrcf2\\sa160\\par\n");
                break;
            case MdSource m:
                One(new CodeBlock(m.Text), sb, indent);
                break;
        }
    }

    private static void QuoteBlocks(List<Block> blocks, StringBuilder sb, int indent)
    {
        foreach (var b in blocks)
        {
            if (b is Para p)
            {
                sb.Append("\\pard").Append(Indent(indent)).Append("\\brdrl\\brdrs\\brdrw20\\brsp100\\brdrcf2\\sa160\\i\\cf3 ");
                Inline(p.Inlines, sb);
                sb.Append("\\i0\\cf0\\par\n");
            }
            else
            {
                One(b, sb, indent);
            }
        }
    }

    private static void ListItems(ListBlock list, StringBuilder sb, int indent)
    {
        var n = list.Start;
        var left = indent + 720;
        foreach (var item in list.Items)
        {
            var marker = list.Ordered ? (n++) + "." : "\\u8226?";
            var first = true;
            foreach (var b in item)
            {
                if (b is Para p)
                {
                    if (first)
                    {
                        sb.Append("\\pard\\li").Append(left).Append("\\fi-360\\sa60 ").Append(marker).Append("\\tab ");
                    }
                    else
                    {
                        sb.Append("\\pard\\li").Append(left).Append("\\sa60 ");
                    }

                    Inline(p.Inlines, sb);
                    sb.Append("\\par\n");
                }
                else
                {
                    if (first)
                    {
                        sb.Append("\\pard\\li").Append(left).Append("\\fi-360\\sa60 ").Append(marker).Append("\\par\n");
                    }

                    One(b, sb, left);
                }

                first = false;
            }

            if (item.Count == 0)
            {
                sb.Append("\\pard\\li").Append(left).Append("\\fi-360\\sa60 ").Append(marker).Append("\\par\n");
            }
        }

        sb.Append("\\pard\\sa60\\par\n");
    }

    private static void Table(TableBlock table, StringBuilder sb)
    {
        var columns = Math.Max(1, table.Rows.Max(r => r.Count));
        var width = PageWidth / columns;
        foreach (var row in table.Rows)
        {
            sb.Append("\\trowd\\trgaph108\\trleft0");
            for (var c = 0; c < columns; c++)
            {
                sb.Append("\\clbrdrt\\brdrs\\brdrw10\\clbrdrl\\brdrs\\brdrw10\\clbrdrb\\brdrs\\brdrw10\\clbrdrr\\brdrs\\brdrw10");
                if (c < row.Count && row[c].Header)
                {
                    sb.Append("\\clcbpat3");
                }

                sb.Append("\\cellx").Append(width * (c + 1));
            }

            sb.Append('\n');
            for (var c = 0; c < columns; c++)
            {
                var cell = c < row.Count ? row[c] : null;
                sb.Append("\\pard\\intbl\\sa0 ");
                if (cell is not null)
                {
                    if (cell.Header)
                    {
                        sb.Append("\\b ");
                    }

                    Inline(cell.Inlines, sb);
                    if (cell.Header)
                    {
                        sb.Append("\\b0 ");
                    }
                }

                sb.Append("\\cell\n");
            }

            sb.Append("\\row\n");
        }

        sb.Append("\\pard\\sa160\\par\n");
    }

    private static void Inline(List<Inline> inlines, StringBuilder sb)
    {
        foreach (var i in inlines)
        {
            if (i.IsBreak)
            {
                sb.Append("\\line ");
                continue;
            }

            var isLink = i.Href is { } h && i.NoteId is null && DocHtml.IsSafeHref(h);
            var props = new StringBuilder();
            if (i.Bold)
            {
                props.Append("\\b");
            }

            if (i.Italic)
            {
                props.Append("\\i");
            }

            if (i.Underline || isLink)
            {
                props.Append("\\ul");
            }

            if (i.Strike)
            {
                props.Append("\\strike");
            }

            if (i.Code)
            {
                props.Append("\\f1\\fs20");
            }

            if (isLink)
            {
                props.Append("\\cf1");
            }

            var text = Escape(i.Text);
            var run = props.Length == 0 ? text : "{" + props + " " + text + "}";
            if (isLink)
            {
                var url = (Uri.TryCreate(i.Href!.Trim(), UriKind.Absolute, out var uri) ? uri.AbsoluteUri : string.Empty).Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}").Replace("\"", "%22");
                sb.Append("{\\field{\\*\\fldinst{HYPERLINK \"").Append(url).Append("\"}}{\\fldrslt ").Append(run).Append("}}");
            }
            else
            {
                sb.Append(run);
            }
        }
    }
}
