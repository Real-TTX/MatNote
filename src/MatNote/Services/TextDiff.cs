namespace MatNote.Services;

/// <summary>One line of a comparison: <c>' '</c> the same, <c>'+'</c> only in the newer text, <c>'-'</c> only in the older one, <c>'…'</c> a stretch of equal lines that is left out.</summary>
public sealed record DiffLine(char Op, string Text);

/// <summary>A line by line comparison of two texts (longest common subsequence), for the history of a note.</summary>
public static class TextDiff
{
    /// <summary>The most lines (each text) that are compared; a bigger text is not compared (<c>null</c>).</summary>
    public const int MaxLines = 2500;

    public static List<DiffLine>? Lines(string older, string newer)
    {
        var a = Split(older);
        var b = Split(newer);

        // what both texts have at the start and at the end needs no work
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            start++;
        }

        var endA = a.Length;
        var endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
        }

        var n = endA - start;
        var m = endB - start;
        if (n > MaxLines || m > MaxLines)
        {
            return null;
        }

        var table = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                table[i, j] = a[start + i] == b[start + j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var result = new List<DiffLine>();
        for (var i = 0; i < start; i++)
        {
            result.Add(new DiffLine(' ', a[i]));
        }

        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (a[start + x] == b[start + y])
            {
                result.Add(new DiffLine(' ', a[start + x]));
                x++;
                y++;
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                result.Add(new DiffLine('-', a[start + x++]));
            }
            else
            {
                result.Add(new DiffLine('+', b[start + y++]));
            }
        }

        while (x < n)
        {
            result.Add(new DiffLine('-', a[start + x++]));
        }

        while (y < m)
        {
            result.Add(new DiffLine('+', b[start + y++]));
        }

        for (var i = endA; i < a.Length; i++)
        {
            result.Add(new DiffLine(' ', a[i]));
        }

        return result;
    }

    /// <summary>Keeps the changed lines with <paramref name="context"/> equal lines around them; a longer stretch of equal lines becomes one <c>'…'</c> line.</summary>
    public static List<DiffLine> Collapse(List<DiffLine> lines, int context = 2)
    {
        var keep = new bool[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Op == ' ')
            {
                continue;
            }

            for (var k = Math.Max(0, i - context); k <= Math.Min(lines.Count - 1, i + context); k++)
            {
                keep[k] = true;
            }
        }

        var result = new List<DiffLine>();
        var skipped = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (keep[i])
            {
                result.Add(lines[i]);
                skipped = false;
            }
            else if (!skipped)
            {
                result.Add(new DiffLine('…', string.Empty));
                skipped = true;
            }
        }

        return result;
    }

    private static string[] Split(string text) => string.IsNullOrEmpty(text) ? [] : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
