using MatNote.Services;

namespace MatNote.Tests;

/// <summary>The title of a copy: "(copy)", then "(copy 2)", "(copy 3)" ...</summary>
public class DuplicateTests
{
    [Theory]
    [InlineData("Plan", "Plan (copy)")]
    [InlineData("Plan (copy)", "Plan (copy 2)")]
    [InlineData("Plan (copy 2)", "Plan (copy 3)")]
    [InlineData("Plan (copy 9)", "Plan (copy 10)")]
    [InlineData("  Plan  ", "Plan (copy)")]
    [InlineData("Plan (copy) of a copy", "Plan (copy) of a copy (copy)")]
    [InlineData("", " (copy)")]
    public void A_copy_has_the_next_title(string title, string expected) => Assert.Equal(expected, DuplicateService.CopyTitle(title));

    [Fact]
    public void A_long_title_is_cut_so_that_the_copy_fits()
    {
        var copy = DuplicateService.CopyTitle(new string('x', 250));
        Assert.Equal(250, copy.Length);
        Assert.EndsWith(" (copy)", copy);
        var second = DuplicateService.CopyTitle(copy);
        Assert.Equal(250, second.Length);
        Assert.EndsWith(" (copy 2)", second);
    }
}
