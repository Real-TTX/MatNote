using System.Globalization;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MatNote.Services.Export;

/// <summary>Reads a Word file into the document model (headings by style, lists, tables, hyperlinks, bold / italic / underline / strike). Throws on a damaged file: the caller decides.</summary>
internal static class DocxReader
{
    /// <param name="maxChars">Reading stops after this many characters; <paramref name="truncated"/> tells that it did.</param>
    public static List<Block> Read(byte[] data, int maxChars, out bool truncated)
    {
        truncated = false;
        using var stream = new MemoryStream(data, false);
        using var package = WordprocessingDocument.Open(stream, false);
        var main = package.MainDocumentPart ?? throw new InvalidDataException("No main document part.");
        var body = main.Document?.Body ?? throw new InvalidDataException("No document body.");
        var reader = new State(main, maxChars);
        reader.Container(body, reader.Blocks);
        reader.EndList();
        truncated = reader.Truncated;
        return reader.Blocks;
    }

    private sealed class State(MainDocumentPart main, int maxChars)
    {
        private readonly Dictionary<string, string> styleNames = main.StyleDefinitionsPart?.Styles?.Elements<W.Style>()
            .Where(s => s.StyleId?.Value is not null)
            .ToDictionary(s => s.StyleId!.Value!, s => s.StyleName?.Val?.Value ?? string.Empty) ?? [];

        private readonly Numbering? numbering = main.NumberingDefinitionsPart?.Numbering;
        private readonly List<(ListBlock List, int Level)> listStack = [];
        private int chars;

        public List<Block> Blocks { get; } = [];

        public bool Truncated { get; private set; }

        public void Container(OpenXmlElement parent, List<Block> target)
        {
            foreach (var child in parent.ChildElements)
            {
                if (chars > maxChars)
                {
                    Truncated = true;
                    return;
                }

                switch (child)
                {
                    case W.Paragraph p:
                        ParagraphBlock(p, target);
                        break;
                    case W.Table t:
                        EndList();
                        if (ReadTable(t) is { } table)
                        {
                            target.Add(table);
                        }

                        break;
                    case SdtBlock sdt when sdt.SdtContentBlock is { } content:
                        Container(content, target);
                        break;
                }
            }
        }

        public void EndList() => listStack.Clear();

        private string StyleName(W.Paragraph p)
        {
            var id = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            return id is not null && styleNames.TryGetValue(id, out var name) ? name : id ?? string.Empty;
        }

        private void ParagraphBlock(W.Paragraph p, List<Block> target)
        {
            var inlines = Inlines.Normalize(RunsOf(p, null));
            var style = StyleName(p);
            var numId = p.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
            var level = p.ParagraphProperties?.NumberingProperties?.NumberingLevelReference?.Val?.Value ?? 0;
            var isList = numId is > 0 || style.StartsWith("List Bullet", StringComparison.OrdinalIgnoreCase) || style.StartsWith("List Number", StringComparison.OrdinalIgnoreCase);
            if (inlines.Count == 0)
            {
                return;
            }

            chars += inlines.Sum(i => i.Text.Length);
            if (isList)
            {
                var ordered = numId is > 0 ? IsOrdered(numId.Value, level) : style.StartsWith("List Number", StringComparison.OrdinalIgnoreCase);
                AddListItem(inlines, ordered, level, target);
                return;
            }

            EndList();
            var heading = HeadingLevel(p, style);
            target.Add(heading > 0 ? new Heading(heading, inlines) : new Para(inlines));
        }

        private static int HeadingLevel(W.Paragraph p, string style)
        {
            if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (style.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) && int.TryParse(style.AsSpan(8).Trim(), out var n))
            {
                return Math.Clamp(n, 1, 4);
            }

            return p.ParagraphProperties?.OutlineLevel?.Val?.Value is { } o and < 9 ? Math.Clamp(o + 1, 1, 4) : 0;
        }

        private bool IsOrdered(int numId, int level)
        {
            var instance = numbering?.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractId = instance?.AbstractNumId?.Val?.Value;
            var abstractNum = numbering?.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
            var format = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level)?.NumberingFormat?.Val?.Value;
            return format is not null && format != NumberFormatValues.Bullet && format != NumberFormatValues.None;
        }

        private void AddListItem(List<Inline> inlines, bool ordered, int level, List<Block> target)
        {
            while (listStack.Count > 0 && listStack[^1].Level > level)
            {
                listStack.RemoveAt(listStack.Count - 1);
            }

            if (listStack.Count > 0 && listStack[^1].Level == level && listStack[^1].List.Ordered != ordered)
            {
                listStack.RemoveAt(listStack.Count - 1);
            }

            if (listStack.Count == 0 || listStack[^1].Level < level)
            {
                var list = new ListBlock(ordered, []);
                if (listStack.Count > 0 && listStack[^1].List.Items.Count > 0)
                {
                    listStack[^1].List.Items[^1].Add(list);
                }
                else
                {
                    target.Add(list);
                }

                listStack.Add((list, level));
            }

            listStack[^1].List.Items.Add([new Para(inlines)]);
        }

        private List<Inline> RunsOf(OpenXmlElement parent, string? href)
        {
            var result = new List<Inline>();
            foreach (var child in parent.ChildElements)
            {
                switch (child)
                {
                    case W.Run run:
                        RunInlines(run, href, result);
                        break;
                    case W.Hyperlink link:
                    {
                        string? target = null;
                        if (link.Id?.Value is { } rid)
                        {
                            var rel = main.HyperlinkRelationships.FirstOrDefault(r => r.Id == rid);
                            target = rel?.Uri.OriginalString;
                        }

                        result.AddRange(RunsOf(link, target));
                        break;
                    }

                    case DeletedRun or W.ParagraphProperties or W.ProofError or BookmarkStart or BookmarkEnd:
                        break;
                    case OpenXmlElement other when other.HasChildren:
                        result.AddRange(RunsOf(other, href));
                        break;
                }
            }

            return result;
        }

        private static bool On(OnOffType? flag) => flag is not null && (flag.Val is null || flag.Val.Value);

        private static void RunInlines(W.Run run, string? href, List<Inline> result)
        {
            var rp = run.RunProperties;
            var bold = On(rp?.Bold);
            var italic = On(rp?.Italic);
            var strike = On(rp?.Strike);
            var underline = rp?.Underline?.Val is { } u && u != UnderlineValues.None;
            Inline Make(string text) => new(text, bold, italic, underline, strike, false, href);
            foreach (var c in run.ChildElements)
            {
                switch (c)
                {
                    case W.Text t:
                        result.Add(Make(t.Text));
                        break;
                    case TabChar:
                        result.Add(Make("\t"));
                        break;
                    case W.Break b when b.Type is null || b.Type.Value == BreakValues.TextWrapping:
                    case CarriageReturn:
                        result.Add(Inline.Break);
                        break;
                    case NoBreakHyphen:
                        result.Add(Make("-"));
                        break;
                }
            }
        }

        private TableBlock? ReadTable(W.Table table)
        {
            var firstRowHeader = table.GetFirstChild<TableProperties>()?.TableLook?.FirstRow?.Value == true;
            var rows = new List<List<Cell>>();
            foreach (var tr in table.Elements<TableRow>())
            {
                var cells = new List<Cell>();
                foreach (var tc in tr.Elements<TableCell>())
                {
                    var inlines = new List<Inline>();
                    foreach (var p in tc.Descendants<W.Paragraph>())
                    {
                        var line = Inlines.Normalize(RunsOf(p, null));
                        if (line.Count == 0)
                        {
                            continue;
                        }

                        if (inlines.Count > 0)
                        {
                            inlines.Add(Inline.Break);
                        }

                        inlines.AddRange(line);
                    }

                    chars += inlines.Sum(i => i.Text.Length);
                    cells.Add(new Cell(firstRowHeader && rows.Count == 0, inlines));
                }

                if (cells.Count > 0)
                {
                    rows.Add(cells);
                }
            }

            if (rows.Count == 0)
            {
                return null;
            }

            var width = rows.Max(r => r.Count);
            foreach (var r in rows)
            {
                while (r.Count < width)
                {
                    r.Add(new Cell(false, []));
                }
            }

            return new TableBlock(rows);
        }
    }
}

/// <summary>A sheet of a workbook as text cells.</summary>
internal sealed record SheetData(string Name, List<List<string>> Rows, bool Truncated);

internal static class XlsxReader
{
    /// <summary>
    /// Reads the sheets of a workbook (<paramref name="firstOnly"/>: only the first). At most <paramref name="maxRows"/> x <paramref name="maxCols"/> cells per sheet.
    /// <paramref name="display"/> gives the values as Excel shows them (number formats), otherwise as plain invariant values (CSV).
    /// </summary>
    public static List<SheetData> Read(byte[] data, int maxRows, int maxCols, bool display, bool firstOnly = false)
    {
        using var stream = new MemoryStream(data, false);
        using var workbook = new XLWorkbook(stream);
        var sheets = new List<SheetData>();
        var worksheets = workbook.Worksheets.ToList();
        if (!firstOnly)
        {
            var visible = worksheets.Where(w => w.Visibility == XLWorksheetVisibility.Visible).ToList();
            worksheets = visible.Count > 0 ? visible : worksheets;
        }

        foreach (var ws in worksheets)
        {
            sheets.Add(ReadSheet(ws, maxRows, maxCols, display));
            if (firstOnly)
            {
                break;
            }
        }

        return sheets;
    }

    private static SheetData ReadSheet(IXLWorksheet ws, int maxRows, int maxCols, bool display)
    {
        var last = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
        var truncated = last > maxRows;
        var rowCount = Math.Min(last, maxRows);
        var rows = new List<List<string>>();
        var width = 0;
        for (var r = 1; r <= rowCount; r++)
        {
            var row = new List<string>();
            foreach (var cell in ws.Row(r).CellsUsed(XLCellsUsedOptions.Contents))
            {
                var col = cell.Address.ColumnNumber;
                if (col > maxCols)
                {
                    truncated = true;
                    break;
                }

                while (row.Count < col - 1)
                {
                    row.Add(string.Empty);
                }

                row.Add(CellText(cell, display));
            }

            width = Math.Max(width, row.Count);
            rows.Add(row);
        }

        foreach (var row in rows)
        {
            while (row.Count < width)
            {
                row.Add(string.Empty);
            }
        }

        return new SheetData(ws.Name, rows, truncated);
    }

    private static string CellText(IXLCell cell, bool display)
    {
        try
        {
            var v = cell.Value;
            if (display)
            {
                return Inlines.Strip(cell.GetFormattedString());
            }

            return Inlines.Strip(v.Type switch
            {
                XLDataType.Blank => string.Empty,
                XLDataType.Boolean => v.GetBoolean() ? "TRUE" : "FALSE",
                XLDataType.Number => v.GetNumber().ToString("R", CultureInfo.InvariantCulture),
                XLDataType.DateTime => Date(v.GetDateTime()),
                XLDataType.TimeSpan => v.GetTimeSpan().ToString("c", CultureInfo.InvariantCulture),
                XLDataType.Error => v.GetError().ToString(),
                _ => v.GetText(),
            });
        }
        catch (Exception)
        {
            // a formula that cannot be evaluated: show what Excel stored, or nothing
            try
            {
                return Inlines.Strip(cell.CachedValue.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    private static string Date(DateTime d) => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The sheets as a document: the name of the sheet as a heading, the cells as a table.</summary>
    public static List<Block> ToBlocks(List<SheetData> sheets, int maxRows, int maxCols)
    {
        var blocks = new List<Block>();
        foreach (var s in sheets)
        {
            blocks.Add(new Heading(2, [new Inline(s.Name)]));
            if (s.Rows.Count == 0 || s.Rows.All(r => r.Count == 0))
            {
                blocks.Add(new Para([new Inline("(empty sheet)", Italic: true)]));
                continue;
            }

            blocks.Add(new TableBlock([.. s.Rows.Select(r => r.Select(c => new Cell(false, c.Length == 0 ? [] : [new Inline(c)])).ToList())]));
            if (s.Truncated)
            {
                blocks.Add(new Para([new Inline($"Only the first {maxRows} rows and {maxCols} columns are shown.", Italic: true)]));
            }
        }

        return blocks;
    }

    /// <summary>CSV (RFC 4180): comma, CRLF, quotes where needed.</summary>
    public static string ToCsv(SheetData sheet)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in sheet.Rows)
        {
            sb.Append(string.Join(',', row.Select(Quote))).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
}

internal static class PdfText
{
    /// <summary>The text of all pages in reading order, a blank line between pages, at most <paramref name="maxChars"/> characters.</summary>
    public static string Extract(byte[] data, int maxChars)
    {
        using var pdf = PdfDocument.Open(data);
        var sb = new System.Text.StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            string text;
            try
            {
                text = ContentOrderTextExtractor.GetText(page);
            }
            catch (Exception)
            {
                text = page.Text;
            }

            if (sb.Length > 0)
            {
                sb.Append("\n\n");
            }

            sb.Append(text.Trim());
            if (sb.Length >= maxChars)
            {
                break;
            }
        }

        return Inlines.Strip(sb.Length > maxChars ? sb.ToString(0, maxChars) : sb.ToString());
    }
}
