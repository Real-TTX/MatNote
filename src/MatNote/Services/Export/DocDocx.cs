using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MatNote.Services.Export;

/// <summary>The model as a Word document: real styles (Title, Heading1-4, Quote, Code, ListParagraph, Hyperlink, TableGrid), real numbering, real hyperlinks.</summary>
internal sealed class DocDocx
{
    private readonly MainDocumentPart main;
    private readonly List<(int NumId, int Level, int Start)> orderedLists = [];
    private int nextNumId = 2; // 1 is the bullet list

    private DocDocx(MainDocumentPart main) => this.main = main;

    private sealed record Ctx(int ListDepth = -1, int QuoteLevel = 0)
    {
        public int Indent => (ListDepth >= 0 ? 720 * (ListDepth + 1) : 0) + (QuoteLevel * 567);
    }

    public static byte[] Render(Doc doc)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            package.PackageProperties.Title = Inlines.Strip(doc.Title);
            package.PackageProperties.Creator = "MatNotes";
            var main = package.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            main.AddNewPart<StyleDefinitionsPart>().Styles = DocxStyles.Build();

            var writer = new DocDocx(main);
            if (doc.NeedsTitleHeading)
            {
                body.Append(MakeParagraph("Title", [MakeRun(new Inline(Inlines.Strip(doc.Title.Trim())), false)]));
            }

            writer.Blocks(doc.Blocks, body, new Ctx());
            body.Append(new Paragraph()); // a table must not be the last element
            body.Append(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 },
                new PageMargin { Top = 1417, Right = 1417, Bottom = 1417, Left = 1417, Header = 708, Footer = 708, Gutter = 0 }));
            main.AddNewPart<NumberingDefinitionsPart>().Numbering = DocxStyles.BuildNumbering(writer.orderedLists);
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private void Blocks(List<Block> blocks, OpenXmlElement parent, Ctx ctx)
    {
        foreach (var b in blocks)
        {
            One(b, parent, ctx);
        }
    }

    private static Paragraph MakeParagraph(string? style, IEnumerable<OpenXmlElement> content, int? numId = null, int level = 0, int indent = 0, bool keepNext = false, int? after = null)
    {
        var props = new ParagraphProperties();
        if (style is not null)
        {
            props.Append(new ParagraphStyleId { Val = style });
        }

        if (keepNext)
        {
            props.Append(new KeepNext());
        }

        if (numId is { } n)
        {
            props.Append(new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = n }));
        }

        if (after is { } a)
        {
            props.Append(new SpacingBetweenLines { After = a.ToString() });
        }

        if (indent > 0 && numId is null)
        {
            props.Append(new Indentation { Left = indent.ToString() });
        }

        var p = new Paragraph();
        if (props.HasChildren)
        {
            p.Append(props);
        }

        p.Append(content);
        return p;
    }

    private void One(Block block, OpenXmlElement parent, Ctx ctx)
    {
        switch (block)
        {
            case Heading h:
                parent.Append(MakeParagraph($"Heading{Math.Clamp(h.Level, 1, 4)}", Runs(h.Inlines), keepNext: true));
                break;
            case Para p:
                parent.Append(MakeParagraph(ctx.QuoteLevel > 0 ? "Quote" : null, Runs(p.Inlines), indent: ctx.Indent));
                break;
            case CodeBlock c:
                parent.Append(MakeParagraph("Code", Runs([new Inline(c.Text.Replace("\r\n", "\n"))]), indent: ctx.Indent));
                break;
            case Rule:
                parent.Append(RuleParagraph());
                break;
            case Quote q:
                Blocks(q.Blocks, parent, ctx with { QuoteLevel = ctx.QuoteLevel + 1 });
                break;
            case ListBlock l:
                ListItems(l, parent, ctx);
                break;
            case TableBlock t:
                WriteTable(t, parent);
                break;
            case Section s:
                parent.Append(MakeParagraph($"Heading{s.Level}", [MakeRun(new Inline(Inlines.Strip(s.Title.Trim())), false)], keepNext: true));
                Blocks(s.Blocks, parent, ctx);
                parent.Append(RuleParagraph());
                break;
            case MdSource m:
                One(new CodeBlock(m.Text), parent, ctx);
                break;
        }
    }

    private static Paragraph RuleParagraph() =>
        new(
            new ParagraphProperties(
                new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Space = 1, Color = "A6A6A6" }),
                new SpacingBetweenLines { Before = "60", After = "160" }));

    private void ListItems(ListBlock list, OpenXmlElement parent, Ctx ctx)
    {
        var depth = Math.Min(ctx.ListDepth + 1, 8);
        int numId;
        if (list.Ordered)
        {
            numId = nextNumId++;
            orderedLists.Add((numId, depth, list.Start));
        }
        else
        {
            numId = 1;
        }

        var inner = ctx with { ListDepth = depth };
        foreach (var item in list.Items)
        {
            var first = true;
            foreach (var b in item)
            {
                if (first && b is Para p)
                {
                    parent.Append(MakeParagraph("ListParagraph", Runs(p.Inlines), numId, depth));
                }
                else
                {
                    if (first)
                    {
                        parent.Append(MakeParagraph("ListParagraph", [], numId, depth));
                    }

                    One(b, parent, inner);
                }

                first = false;
            }

            if (item.Count == 0)
            {
                parent.Append(MakeParagraph("ListParagraph", [], numId, depth));
            }
        }
    }

    private void WriteTable(TableBlock table, OpenXmlElement parent)
    {
        var columns = Math.Max(1, table.Rows.Max(r => r.Count));
        var width = 9000 / columns;
        var t = new Table(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = false, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true }));
        var grid = new TableGrid();
        for (var c = 0; c < columns; c++)
        {
            grid.Append(new GridColumn { Width = width.ToString() });
        }

        t.Append(grid);
        foreach (var row in table.Rows)
        {
            var tr = new TableRow();
            for (var c = 0; c < columns; c++)
            {
                var cell = c < row.Count ? row[c] : new Cell(false, []);
                var props = new TableCellProperties(new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });
                if (cell.Header)
                {
                    props.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "EDEDED" });
                }

                tr.Append(new TableCell(props, MakeParagraph(null, Runs(cell.Inlines, cell.Header), after: 0)));
            }

            t.Append(tr);
        }

        parent.Append(t);
        parent.Append(MakeParagraph(null, [], after: 0));
    }

    private List<OpenXmlElement> Runs(List<Inline> inlines, bool bold = false)
    {
        var result = new List<OpenXmlElement>();
        var k = 0;
        while (k < inlines.Count)
        {
            var i = inlines[k];
            if (i.IsBreak)
            {
                result.Add(new Run(new Break()));
                k++;
            }
            else if (i.Href is { } href && i.NoteId is null && DocHtml.IsSafeHref(href) && Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri))
            {
                var link = new Hyperlink { Id = main.AddHyperlinkRelationship(uri, true).Id, History = true };
                while (k < inlines.Count && !inlines[k].IsBreak && inlines[k].Href == href && inlines[k].NoteId is null)
                {
                    link.Append(MakeRun(inlines[k], bold, true));
                    k++;
                }

                result.Add(link);
            }
            else
            {
                result.Add(MakeRun(i, bold));
                k++;
            }
        }

        return result;
    }

    private static Run MakeRun(Inline i, bool bold, bool hyperlink = false)
    {
        var props = new RunProperties();
        if (hyperlink)
        {
            props.Append(new RunStyle { Val = "Hyperlink" });
        }
        else if (i.Code)
        {
            props.Append(new RunStyle { Val = "CodeChar" });
        }

        if (hyperlink && i.Code)
        {
            props.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" });
        }

        if (i.Bold || bold)
        {
            props.Append(new Bold());
        }

        if (i.Italic)
        {
            props.Append(new Italic());
        }

        if (i.Strike)
        {
            props.Append(new Strike());
        }

        if (i.Underline && !hyperlink)
        {
            props.Append(new Underline { Val = UnderlineValues.Single });
        }

        var run = new Run();
        if (props.HasChildren)
        {
            run.Append(props);
        }

        var buffer = new System.Text.StringBuilder();
        void FlushText()
        {
            if (buffer.Length > 0)
            {
                run.Append(new Text(buffer.ToString()) { Space = SpaceProcessingModeValues.Preserve });
                buffer.Clear();
            }
        }

        foreach (var c in Inlines.Strip(i.Text))
        {
            switch (c)
            {
                case '\t':
                    FlushText();
                    run.Append(new TabChar());
                    break;
                case '\n':
                    FlushText();
                    run.Append(new Break());
                    break;
                case '\r':
                    break;
                default:
                    buffer.Append(c);
                    break;
            }
        }

        FlushText();
        return run;
    }
}
