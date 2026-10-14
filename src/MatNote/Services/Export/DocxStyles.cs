using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MatNote.Services.Export;

/// <summary>The style sheet and the numbering definitions of the Word export.</summary>
internal static class DocxStyles
{
    public static Styles Build()
    {
        var styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" },
                    new FontSize { Val = "22" },
                    new FontSizeComplexScript { Val = "22" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "160", Line = "264", LineRule = LineSpacingRuleValues.Auto }))));

        styles.Append(new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, Default = true, StyleId = "Normal" });
        styles.Append(new Style(
            new StyleName { Val = "Default Paragraph Font" }, new UIPriority { Val = 1 }, new SemiHidden(), new UnhideWhenUsed())
        { Type = StyleValues.Character, Default = true, StyleId = "DefaultParagraphFont" });

        styles.Append(new Style(
            new StyleName { Val = "Title" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new UIPriority { Val = 10 }, new PrimaryStyle(),
            new StyleParagraphProperties(new SpacingBetweenLines { After = "240" }),
            new StyleRunProperties(new Bold(), new Color { Val = "1F3864" }, new FontSize { Val = "48" }, new FontSizeComplexScript { Val = "48" }))
        { Type = StyleValues.Paragraph, StyleId = "Title" });

        var sizes = new[] { 36, 30, 26, 24 };
        for (var n = 1; n <= 4; n++)
        {
            styles.Append(new Style(
                new StyleName { Val = "heading " + n }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new UIPriority { Val = 9 }, new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new KeepLines(),
                    new SpacingBetweenLines { Before = "280", After = "100" },
                    new OutlineLevel { Val = n - 1 }),
                new StyleRunProperties(new Bold(), new Color { Val = "1F3864" }, new FontSize { Val = sizes[n - 1].ToString() }, new FontSizeComplexScript { Val = sizes[n - 1].ToString() }))
            { Type = StyleValues.Paragraph, StyleId = "Heading" + n });
        }

        styles.Append(new Style(
            new StyleName { Val = "Hyperlink" }, new BasedOn { Val = "DefaultParagraphFont" }, new UIPriority { Val = 99 }, new UnhideWhenUsed(),
            new StyleRunProperties(new Color { Val = "0563C1" }, new Underline { Val = UnderlineValues.Single }))
        { Type = StyleValues.Character, StyleId = "Hyperlink" });

        styles.Append(new Style(
            new StyleName { Val = "Code Char" }, new BasedOn { Val = "DefaultParagraphFont" }, new UIPriority { Val = 20 },
            new StyleRunProperties(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" },
                new FontSize { Val = "20" },
                new FontSizeComplexScript { Val = "20" }))
        { Type = StyleValues.Character, StyleId = "CodeChar" });

        styles.Append(new Style(
            new StyleName { Val = "Code" }, new BasedOn { Val = "Normal" }, new UIPriority { Val = 20 }, new PrimaryStyle(),
            new StyleParagraphProperties(
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F2F2F2" },
                new SpacingBetweenLines { After = "160", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
            new StyleRunProperties(
                new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" },
                new FontSize { Val = "20" },
                new FontSizeComplexScript { Val = "20" }))
        { Type = StyleValues.Paragraph, StyleId = "Code" });

        styles.Append(new Style(
            new StyleName { Val = "Quote" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new UIPriority { Val = 29 }, new PrimaryStyle(),
            new StyleParagraphProperties(
                new ParagraphBorders(new LeftBorder { Val = BorderValues.Single, Size = 18, Space = 8, Color = "BFBFBF" }),
                new Indentation { Left = "567" }),
            new StyleRunProperties(new Italic(), new ItalicComplexScript(), new Color { Val = "595959" }))
        { Type = StyleValues.Paragraph, StyleId = "Quote" });

        styles.Append(new Style(
            new StyleName { Val = "List Paragraph" }, new BasedOn { Val = "Normal" }, new UIPriority { Val = 34 }, new PrimaryStyle(),
            new StyleParagraphProperties(new SpacingBetweenLines { After = "60" }, new Indentation { Left = "720" }))
        { Type = StyleValues.Paragraph, StyleId = "ListParagraph" });

        styles.Append(new Style(
            new StyleName { Val = "Table Grid" }, new UIPriority { Val = 39 },
            new StyleParagraphProperties(new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }),
            new StyleTableProperties(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Space = 0, Color = "auto" })))
        { Type = StyleValues.Table, StyleId = "TableGrid" });
        return styles;
    }

    /// <summary>Numbering: id 1 is the bullet list, every ordered list has an id of its own (so that each starts again).</summary>
    public static Numbering BuildNumbering(List<(int NumId, int Level, int Start)> ordered)
    {
        var bullets = new AbstractNum { AbstractNumberId = 1 };
        bullets.Append(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel });
        var numbers = new AbstractNum { AbstractNumberId = 2 };
        numbers.Append(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel });
        var bulletChars = new[] { "•", "◦", "▪" };
        var formats = new[] { NumberFormatValues.Decimal, NumberFormatValues.LowerLetter, NumberFormatValues.LowerRoman };
        for (var lvl = 0; lvl < 9; lvl++)
        {
            bullets.Append(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = bulletChars[lvl % 3] },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = (720 * (lvl + 1)).ToString(), Hanging = "360" }))
            { LevelIndex = lvl });
            numbers.Append(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = formats[lvl % 3] },
                new LevelText { Val = $"%{lvl + 1}." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(new Indentation { Left = (720 * (lvl + 1)).ToString(), Hanging = "360" }))
            { LevelIndex = lvl });
        }

        var numbering = new Numbering(bullets, numbers);
        numbering.Append(new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
        foreach (var (numId, level, start) in ordered)
        {
            numbering.Append(new NumberingInstance(
                new AbstractNumId { Val = 2 },
                new LevelOverride(new StartOverrideNumberingValue { Val = start }) { LevelIndex = level })
            { NumberID = numId });
        }

        return numbering;
    }
}
