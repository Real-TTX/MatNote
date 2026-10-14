using MatNote.Services.Export;

namespace MatNote.Tests.Export;

public class PdfConcurrencyTests
{
    /// <summary>Regression: QuestPDF documents made at the same moment lost characters (bold text read back as NUL) until the exporter made one at a time.</summary>
    [Fact]
    public async Task Pdfs_made_at_the_same_time_are_all_intact()
    {
        var note = Kit.Rich(1, "Markdown Note", "<h2>Überschrift Markdown</h2><p>Text mit <strong>fett</strong> <em>kursiv</em> und <code>code</code></p>");
        var pdfs = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => Kit.Export(note, ExportFormat.Pdf))));
        foreach (var pdf in pdfs)
        {
            var text = Kit.Squash(Kit.PdfText(pdf.Data));
            Assert.DoesNotContain('\0', text);
            Assert.Contains("ÜberschriftMarkdown", text);
            Assert.Contains("Textmitfettkursivundcode", text);
        }
    }
}
