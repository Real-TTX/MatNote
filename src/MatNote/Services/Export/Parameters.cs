using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace MatNote.Services.Export;

/// <summary>
/// Parameters of a note: <c>{{name}}</c> or <c>{{name|default}}</c> in its text (a prompt with a place for the text to work on). When a note is copied or exported the person is asked for the
/// values (the page does that, see params.js); the export then writes them in place of the placeholders, in every format. A placeholder without a value keeps its default, or stays as it is.
/// The linked notes that are copied in bring their placeholders along, so a prompt built from snippets asks for all of them at once.
/// </summary>
public static partial class Parameters
{
    /// <summary>The prefix of a value in an address: <c>?p_message=Hello</c>.</summary>
    public const string QueryPrefix = "p_";

    [GeneratedRegex(@"\{\{\s*([^{}|]{1,40}?)\s*(?:\|([^{}]*))?\}\}")]
    private static partial Regex Placeholder();

    /// <summary>The values in an address (<c>p_name=value</c>), or <c>null</c> when it has none.</summary>
    public static IReadOnlyDictionary<string, string>? FromQuery(IQueryCollection query)
    {
        Dictionary<string, string>? values = null;
        foreach (var (key, value) in query)
        {
            if (key.Length > QueryPrefix.Length && key.StartsWith(QueryPrefix, StringComparison.Ordinal))
            {
                values ??= new Dictionary<string, string>(StringComparer.Ordinal);
                values[key[QueryPrefix.Length..].Trim()] = value.ToString();
            }
        }

        return values;
    }

    /// <summary>The names in a text, in the order they appear, each once.</summary>
    public static List<string> Names(string text) => Placeholder().Matches(text ?? string.Empty).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        return Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Groups[2].Success ? m.Groups[2].Value : m.Value);
    }

    internal static List<Block> Fill(List<Block> blocks, IReadOnlyDictionary<string, string> values) => [.. blocks.Select(b => Fill(b, values))];

    private static List<Inline> FillInlines(List<Inline> inlines, IReadOnlyDictionary<string, string> values)
        => [.. inlines.Select(i => i.IsBreak ? i : i with { Text = Fill(i.Text, values) })];

    private static Block Fill(Block block, IReadOnlyDictionary<string, string> values) => block switch
    {
        Heading h => h with { Inlines = FillInlines(h.Inlines, values) },
        Para p => p with { Inlines = FillInlines(p.Inlines, values) },
        ListBlock l => l with { Items = [.. l.Items.Select(item => Fill(item, values))] },
        Quote q => new Quote(Fill(q.Blocks, values)),
        CodeBlock c => new CodeBlock(Fill(c.Text, values)),
        TableBlock t => new TableBlock([.. t.Rows.Select(row => row.Select(c => c with { Inlines = FillInlines(c.Inlines, values) }).ToList())]),
        Section s => s with { Title = Fill(s.Title, values), Blocks = Fill(s.Blocks, values) },
        MdSource m => m with { Text = Fill(m.Text, values) },
        _ => block,
    };
}
