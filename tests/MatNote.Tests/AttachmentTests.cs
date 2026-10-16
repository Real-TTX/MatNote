using MatNote.Services;

namespace MatNote.Tests;

/// <summary>The name an attachment is kept under and its icon: nothing from the uploader's file system gets through.</summary>
public class AttachmentTests
{
    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("C:\\Users\\me\\Desktop\\plan.xlsx", "plan.xlsx")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("  spaced name.txt  ", "spaced name.txt")]
    [InlineData("", "file")]
    [InlineData(null, "file")]
    [InlineData("a/b/", "file")]
    public void Name_is_a_plain_file_name(string? given, string expected) => Assert.Equal(expected, AttachmentService.CleanName(given));

    [Fact]
    public void Control_characters_are_dropped()
        => Assert.Equal("ab.txt", AttachmentService.CleanName("a\u0000b\r\n.txt"));

    [Fact]
    public void A_long_name_keeps_its_extension_and_fits_the_column()
    {
        var name = AttachmentService.CleanName(new string('x', 400) + ".docx");
        Assert.Equal(260, name.Length);
        Assert.EndsWith(".docx", name);
    }

    [Theory]
    [InlineData("a.docx", "file-text")]
    [InlineData("a.xlsx", "table")]
    [InlineData("a.pdf", "book")]
    [InlineData("a.PNG", "image")]
    [InlineData("a.zip", "archive")]
    [InlineData("a.bin", "paperclip")]
    [InlineData("a.txt", "file")]
    public void Icon_follows_the_kind_of_file(string name, string icon) => Assert.Equal(icon, AttachmentService.IconFor(name));
}
