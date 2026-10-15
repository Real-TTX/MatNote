using System.Text.RegularExpressions;
using MatNote.Data;

namespace MatNote.Services.Export;

/// <summary>
/// Replaces the links to other notes in a document: the link text stays where it was (plain text, no dead link) and, when asked, the linked note is put after the paragraph (or
/// the line of a plain text) as a titled <see cref="Section"/>. A note is copied once at most, the note itself never, at most <see cref="ExportOptions.MaxDepth"/> levels deep,
/// and only when the <see cref="NoteResolver"/> gives it (it does not for a note the person may not read).
/// </summary>
internal sealed class LinkInliner(NoteResolver resolver, ExportOptions options, bool markdownSource, long rootId)
{
    private readonly HashSet<long> visited = [rootId];
    private readonly Dictionary<long, Note?> resolved = [];

    public Task<List<Block>> ProcessAsync(List<Block> blocks, CancellationToken ct) => Process(blocks, 0, ct);

    private async Task<List<Block>> Process(List<Block> blocks, int depth, CancellationToken ct)
    {
        var result = new List<Block>();
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            switch (block)
            {
                case Para { LineMode: true } p when p.Inlines.Any(i => i.NoteId is not null):
                    await ProcessLines(p, depth, result, ct);
                    break;
                case Para p:
                {
                    var (inlines, ids) = Strip(p.Inlines);
                    result.Add(p with { Inlines = inlines });
                    await AddSections(ids, depth, result, ct);
                    break;
                }

                case Heading h:
                {
                    var (inlines, ids) = Strip(h.Inlines);
                    result.Add(h with { Inlines = inlines });
                    await AddSections(ids, depth, result, ct);
                    break;
                }

                case ListBlock l:
                {
                    var items = new List<List<Block>>();
                    foreach (var item in l.Items)
                    {
                        items.Add(await Process(item, depth, ct));
                    }

                    result.Add(l with { Items = items });
                    break;
                }

                case Quote q:
                    result.Add(new Quote(await Process(q.Blocks, depth, ct)));
                    break;
                case TableBlock t:
                {
                    var ids = new List<long>();
                    var rows = new List<List<Cell>>();
                    foreach (var row in t.Rows)
                    {
                        rows.Add([.. row.Select(c =>
                        {
                            var (inlines, cellIds) = Strip(c.Inlines);
                            ids.AddRange(cellIds);
                            return c with { Inlines = inlines };
                        })]);
                    }

                    result.Add(new TableBlock(rows));
                    await AddSections(ids, depth, result, ct);
                    break;
                }

                case MdSource { IsCode: false } m:
                {
                    var ids = new List<long>();
                    var text = MarkdownService.NoteToken().Replace(m.Text, match =>
                    {
                        ids.Add(long.Parse(match.Groups[2].Value));
                        var title = match.Groups[1].Value.Trim();
                        return title.Length == 0 ? $"Note {match.Groups[2].Value}" : title;
                    });
                    result.Add(m with { Text = text });
                    await AddSections(ids, depth, result, ct);
                    break;
                }

                default:
                    result.Add(block);
                    break;
            }
        }

        return result;
    }

    /// <summary>A plain text paragraph: the linked note goes directly after the line that has the link.</summary>
    private async Task ProcessLines(Para para, int depth, List<Block> result, CancellationToken ct)
    {
        var lines = new List<List<Inline>> { new() };
        foreach (var i in para.Inlines)
        {
            if (i.IsBreak)
            {
                lines.Add([]);
            }
            else
            {
                lines[^1].Add(i);
            }
        }

        var group = new List<Inline>();
        foreach (var line in lines)
        {
            var (inlines, ids) = Strip(line);
            if (group.Count > 0)
            {
                group.Add(Inline.Break);
            }

            group.AddRange(inlines);
            if (ids.Count == 0)
            {
                continue;
            }

            var sections = new List<Block>();
            await AddSections(ids, depth, sections, ct);
            if (sections.Count > 0)
            {
                result.Add(new Para(Inlines.Normalize(group, collapse: false), true));
                result.AddRange(sections);
                group = [];
            }
        }

        if (group.Count > 0)
        {
            result.Add(new Para(Inlines.Normalize(group, collapse: false), true));
        }
    }

    private static (List<Inline> Inlines, List<long> Ids) Strip(List<Inline> inlines)
    {
        if (!inlines.Any(i => i.NoteId is not null))
        {
            return (inlines, []);
        }

        var ids = new List<long>();
        var plain = new List<Inline>(inlines.Count);
        foreach (var i in inlines)
        {
            if (i.NoteId is { } id)
            {
                ids.Add(id);
                plain.Add(i with { NoteId = null, Href = null });
            }
            else
            {
                plain.Add(i);
            }
        }

        return (Inlines.Normalize(plain, collapse: false), ids);
    }

    private async Task AddSections(List<long> ids, int depth, List<Block> into, CancellationToken ct)
    {
        if (!options.IncludeLinkedNotes || depth >= options.MaxDepth)
        {
            return;
        }

        foreach (var id in ids)
        {
            if (visited.Contains(id))
            {
                continue;
            }

            if (!resolved.TryGetValue(id, out var note))
            {
                note = await resolver(id);
                resolved[id] = note;
            }

            if (note is null || !NoteDocs.IsText(note.Type) || !visited.Add(note.Id))
            {
                continue;
            }

            var content = await Process(NoteDocs.Blocks(note, markdownSource), depth + 1, ct);
            into.Add(new Section(string.IsNullOrWhiteSpace(note.Title) ? $"Note {note.Id}" : note.Title.Trim(), content, depth + 1, note.Id));
        }
    }
}
