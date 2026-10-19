using MatNote.Data;
using MatNote.Services.Export;

namespace MatNote.Tests.Export;

/// <summary>Parameters of a note: <c>{{name}}</c> and <c>{{name|default}}</c> are filled when it is exported, in every format.</summary>
public class ParametersTests
{
    private static ExportOptions With(params (string Name, string Value)[] values) => new(false, 3, false, values.ToDictionary(v => v.Name, v => v.Value));

    [Fact]
    public void Names_come_in_the_order_of_the_text_each_once()
        => Assert.Equal(["text", "tone", "a b"], Parameters.Names("{{text}} and {{ tone |friendly}} then {{text}} {{a b}} {{}} {not}"));

    [Fact]
    public void A_value_replaces_the_placeholder_a_default_fills_in_for_a_missing_value_and_the_rest_stays()
    {
        var values = new Dictionary<string, string> { ["text"] = "Hello" };
        Assert.Equal("Hello / friendly / {{other}}", Parameters.Fill("{{text}} / {{tone|friendly}} / {{other}}", values));
    }

    [Fact]
    public void An_empty_value_is_a_value()
        => Assert.Equal("a--b", Parameters.Fill("a-{{x|d}}-b", new Dictionary<string, string> { ["x"] = string.Empty }));

    [Fact]
    public void Values_are_read_from_the_address()
    {
        var query = new Microsoft.AspNetCore.Http.QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["p_message"] = "Hi there", ["format"] = "Txt", ["p_a b"] = "x" });
        var values = Parameters.FromQuery(query)!;
        Assert.Equal(["a b", "message"], values.Keys.Order());
        Assert.Equal("Hi there", values["message"]);
        Assert.Null(Parameters.FromQuery(new Microsoft.AspNetCore.Http.QueryCollection()));
    }

    [Fact]
    public async Task The_export_writes_the_values_into_text_lists_tables_quotes_code_and_the_title()
    {
        var note = Kit.Rich(1, "Prompt for {{who}}", "<p>Dear {{who}},</p><ul><li>{{what}}</li></ul><blockquote>{{who}}</blockquote><pre>{{what}}</pre><table><tr><td>{{who}}</td></tr></table>");
        var result = await Kit.Export(note, ExportFormat.Markdown, With(("who", "Bea"), ("what", "the plan")));
        var text = Kit.Utf8(result);
        Assert.DoesNotContain("{{", text);
        Assert.Contains("Dear Bea,", text);
        Assert.Contains("the plan", text);
        Assert.Equal("Prompt for Bea.md", result.FileName);
    }

    [Fact]
    public async Task Without_values_the_export_leaves_every_placeholder_alone()
    {
        var text = Kit.Utf8(await Kit.Export(Kit.Txt(1, "T", "Say {{x|hi}}"), ExportFormat.Txt));
        Assert.Contains("{{x|hi}}", text);
    }

    [Fact]
    public async Task A_linked_note_that_is_copied_in_is_filled_too()
    {
        var a = Kit.Rich(1, "A", "<p>See " + Kit.Link(2, "Snippet") + "</p>");
        var b = Kit.Rich(2, "Snippet", "<p>Answer in a {{tone}} tone.</p>");
        var html = Kit.Utf8(await Kit.Export(a, ExportFormat.Html, new ExportOptions(true, 3, false, new Dictionary<string, string> { ["tone"] = "calm" }), Kit.Resolver(b)));
        Assert.Contains("Answer in a calm tone.", html);
    }

    [Fact]
    public async Task Markdown_source_is_filled_too()
    {
        var note = Kit.Md(1, "M", "# Title\n\nWrite about {{topic}}.");
        Assert.Contains("Write about cats.", Kit.Utf8(await Kit.Export(note, ExportFormat.Markdown, With(("topic", "cats")))));
    }
}
