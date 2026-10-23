using MatNote.Data;
using MatNote.Services;
using MatNote.Services.Export;

namespace MatNote.Tests;

/// <summary>The history of a note: when a state is kept (not at every autosave), the comparison of two states.</summary>
public class HistoryTests
{
    private static readonly DateTime Now = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_first_change_keeps_the_state_it_replaces()
        => Assert.True(VersionPolicy.IsDue(null, Now.AddSeconds(-2), 1, 1, Now));

    [Fact]
    public void An_autosave_in_the_middle_of_a_sitting_keeps_nothing()
    {
        // saved two seconds ago by the same person, the last snapshot is ten minutes old
        Assert.False(VersionPolicy.IsDue(Now.AddMinutes(-10), Now.AddSeconds(-2), 1, 1, Now));
        Assert.False(VersionPolicy.IsDue(Now.AddMinutes(-10), Now.AddMinutes(-4), 1, 1, Now));
    }

    [Fact]
    public void A_change_after_a_pause_keeps_the_work_of_the_sitting_before()
        => Assert.True(VersionPolicy.IsDue(Now.AddHours(-3), Now.AddMinutes(-6), 1, 1, Now));

    [Fact]
    public void A_sitting_that_goes_on_gets_a_snapshot_every_half_hour()
    {
        Assert.False(VersionPolicy.IsDue(Now.AddMinutes(-29), Now.AddSeconds(-2), 1, 1, Now));
        Assert.True(VersionPolicy.IsDue(Now.AddMinutes(-31), Now.AddSeconds(-2), 1, 1, Now));
    }

    [Fact]
    public void Somebody_elses_state_is_kept_before_it_is_replaced()
        => Assert.True(VersionPolicy.IsDue(Now.AddMinutes(-10), Now.AddSeconds(-30), 2, 1, Now));

    [Fact]
    public void Nothing_is_kept_twice_within_a_minute()
        => Assert.False(VersionPolicy.IsDue(Now.AddSeconds(-30), Now.AddHours(-1), 2, 1, Now));

    [Fact]
    public void A_comparison_shows_what_was_added_and_removed()
    {
        var lines = TextDiff.Lines("one\ntwo\nthree\nfour", "one\n2\nthree\nfour\nfive")!;
        Assert.Equal(["  one", "- two", "+ 2", "  three", "  four", "+ five"], lines.Select(l => $"{l.Op} {l.Text}"));
        Assert.Equal(new[] { ' ', '+' }, TextDiff.Lines("a", "a\nb")!.Select(l => l.Op).Distinct().Order());
        Assert.Empty(TextDiff.Lines(string.Empty, string.Empty)!);
    }

    [Fact]
    public void A_long_stretch_of_equal_lines_is_left_out()
    {
        var older = string.Join('\n', Enumerable.Range(1, 40).Select(i => "line " + i));
        var newer = older.Replace("line 20", "changed");
        var shown = TextDiff.Collapse(TextDiff.Lines(older, newer)!, 2);
        Assert.Equal(['…', ' ', ' ', '-', '+', ' ', ' ', '…'], shown.Select(l => l.Op));
    }

    [Fact]
    public void A_text_that_is_too_big_is_not_compared()
        => Assert.Null(TextDiff.Lines(string.Join('\n', Enumerable.Range(0, TextDiff.MaxLines + 5).Select(i => "a" + i)), "x"));

    [Fact]
    public void A_rich_text_version_is_compared_as_plain_text_lines()
    {
        Assert.Equal("Title\n-----\nSome bold text", VersionText.Of(NoteType.RichText, "<h2>Title</h2><p>Some <b>bold</b> text</p>").Replace("\n\n", "\n"));
        Assert.Equal("a\nb", VersionText.Of(NoteType.PlainText, "a\r\nb\r\n"));
    }
}
