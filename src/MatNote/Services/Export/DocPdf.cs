using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing.Exceptions;

namespace MatNote.Services.Export;

/// <summary>
/// The model as a PDF (A4, selectable text) with QuestPDF. The fonts are the ones of the Docker image (Liberation, DejaVu, see the Dockerfile) with Windows fonts behind them
/// for a developer PC; a character no font has is drawn as a missing-glyph box but never fails the export.
/// </summary>
internal static class DocPdf
{
    // The first font that has a character draws it: Latin from Liberation / Arial, then DejaVu (Greek, Cyrillic, Arabic, symbols), then CJK and emoji fonts if the machine has any.
    private static readonly string[] Sans =
    [
        "Liberation Sans", "DejaVu Sans", "Arial", "Segoe UI",
        "Droid Sans Fallback", "Noto Sans CJK SC", "Noto Sans CJK JP", "WenQuanYi Zen Hei", "Microsoft YaHei", "Yu Gothic", "Segoe UI Symbol", "Segoe UI Emoji", "Noto Color Emoji",
        "Lato",
    ];
    private static readonly string[] Mono = ["Liberation Mono", "DejaVu Sans Mono", "Consolas", "Courier New", "Lato"];

    static DocPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

    // QuestPDF shares its font and glyph caches between documents; two documents made at the same moment produce broken text (missing characters in the
    // extracted text), so one document is made at a time. A PDF takes milliseconds.
    private static readonly object Gate = new();

    public static byte[] Render(Doc doc)
    {
        lock (Gate)
        {
            try
            {
                return Create(doc, Content).GeneratePdf();
            }
            catch (DocumentLayoutException)
            {
                // a layout that cannot be paged (e.g. an absurdly tall table row): fall back to the plain text, which always fits
                return Create(doc, (d, col) => col.Item().Text(DocText.Render(d)).FontFamily(Mono).FontSize(9.5f)).GeneratePdf();
            }
        }
    }

    private static QuestPDF.Fluent.Document Create(Doc doc, Action<Doc, ColumnDescriptor> content) =>
        QuestPDF.Fluent.Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontFamily(Sans).FontSize(11).LineHeight(1.35f));
            page.Content().Column(col =>
            {
                col.Spacing(8);
                content(doc, col);
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Grey.Medium));
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).WithMetadata(new DocumentMetadata { Title = Inlines.Strip(doc.Title), Author = "MatNotes", Creator = "MatNotes" });

    private static void Content(Doc doc, ColumnDescriptor col)
    {
        var any = false;
        if (doc.NeedsTitleHeading)
        {
            col.Item().Text(Inlines.Strip(doc.Title.Trim())).FontSize(22).Bold();
            any = true;
        }

        Blocks(doc.Blocks, col);
        if (!any && doc.Blocks.Count == 0)
        {
            col.Item().Text(" ");
        }
    }

    private static void Blocks(List<Block> blocks, ColumnDescriptor col)
    {
        foreach (var b in blocks)
        {
            One(b, col);
        }
    }

    private static void One(Block block, ColumnDescriptor col)
    {
        switch (block)
        {
            case Heading h:
            {
                var size = h.Level switch { 1 => 19, 2 => 16, 3 => 13.5f, _ => 12 };
                col.Item().EnsureSpace(70).PaddingTop(6).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(size).Bold());
                    Spans(t, h.Inlines);
                });
                break;
            }

            case Para p:
                col.Item().Text(t => Spans(t, p.Inlines));
                break;
            case CodeBlock c:
                col.Item().Background(Colors.Grey.Lighten4).Padding(6).Text(Inlines.Strip(c.Text)).FontFamily(Mono).FontSize(9.5f);
                break;
            case MdSource m:
                col.Item().Background(Colors.Grey.Lighten4).Padding(6).Text(Inlines.Strip(m.Text)).FontFamily(Mono).FontSize(9.5f);
                break;
            case Rule:
                col.Item().PaddingVertical(4).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                break;
            case Quote q:
                col.Item().BorderLeft(2).BorderColor(Colors.Grey.Lighten1).PaddingLeft(10).DefaultTextStyle(x => x.Italic().FontColor(Colors.Grey.Darken2)).Column(c =>
                {
                    c.Spacing(6);
                    Blocks(q.Blocks, c);
                });
                break;
            case ListBlock l:
                ListItems(l, col);
                break;
            case TableBlock t:
                Table(t, col);
                break;
            case Section s:
                col.Item().BorderLeft(3).BorderColor(Colors.Orange.Medium).PaddingLeft(10).Column(c =>
                {
                    c.Spacing(8);
                    One(new Heading(s.Level, [new Inline(s.Title.Trim())]), c);
                    Blocks(s.Blocks, c);
                });
                break;
        }
    }

    private static void ListItems(ListBlock list, ColumnDescriptor col)
    {
        var n = list.Start;
        foreach (var item in list.Items)
        {
            var marker = list.Ordered ? $"{n++}." : "•";
            col.Item().PaddingLeft(6).Row(row =>
            {
                row.ConstantItem(list.Ordered ? 26 : 16).Text(marker);
                row.RelativeItem().Column(c =>
                {
                    c.Spacing(4);
                    Blocks(item, c);
                });
            });
        }
    }

    private static void Table(TableBlock table, ColumnDescriptor col)
    {
        var columns = Math.Max(1, table.Rows.Max(r => r.Count));
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                for (var i = 0; i < columns; i++)
                {
                    c.RelativeColumn();
                }
            });
            foreach (var row in table.Rows)
            {
                for (var i = 0; i < columns; i++)
                {
                    var cell = i < row.Count ? row[i] : new Cell(false, []);
                    var box = t.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten1);
                    if (cell.Header)
                    {
                        box = box.Background(Colors.Grey.Lighten3);
                    }

                    box.Padding(4).Text(x =>
                    {
                        x.DefaultTextStyle(s => cell.Header ? s.FontSize(10).Bold() : s.FontSize(10));
                        Spans(x, cell.Inlines);
                    });
                }
            }
        });
    }

    private static void Spans(TextDescriptor t, List<Inline> inlines)
    {
        foreach (var i in inlines)
        {
            if (i.IsBreak)
            {
                t.Span("\n");
                continue;
            }

            var text = Inlines.Strip(i.Text);
            if (text.Length == 0)
            {
                continue;
            }

            var isLink = i.Href is { } href && i.NoteId is null && DocHtml.IsSafeHref(href);
            var span = isLink ? t.Hyperlink(text, i.Href!.Trim()) : t.Span(text);
            if (isLink)
            {
                span = span.FontColor(Colors.Blue.Darken2).Underline();
            }

            if (i.Bold)
            {
                span = span.Bold();
            }

            if (i.Italic)
            {
                span = span.Italic();
            }

            if (i.Underline)
            {
                span = span.Underline();
            }

            if (i.Strike)
            {
                span = span.Strikethrough();
            }

            if (i.Code)
            {
                span = span.FontFamily(Mono).BackgroundColor(Colors.Grey.Lighten4);
            }
        }
    }
}
