using System.Text;
using System.Text.RegularExpressions;

namespace MatNote.Services.Export;

/// <summary>Names for exported files and zip entries: no path characters, no control characters, no reserved Windows names, a sensible length.</summary>
internal static partial class FileNames
{
    private const int MaxLength = 100;

    [GeneratedRegex("^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase)]
    private static partial Regex Reserved();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();

    /// <summary>A safe file name (with the extension kept, without any directory).</summary>
    public static string Safe(string? name, string fallback = "note", int maxLength = MaxLength)
    {
        var text = (name ?? string.Empty).Trim();
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(char.IsControl(c) || "/\\:*?\"<>|".Contains(c) ? ' ' : c);
        }

        var clean = Spaces().Replace(sb.ToString(), " ").Trim(' ', '.');
        if (clean.Length == 0)
        {
            return fallback;
        }

        var ext = Path.GetExtension(clean);
        if (ext.Length > 12)
        {
            ext = string.Empty;
        }

        var stem = clean[..(clean.Length - ext.Length)].Trim(' ', '.');
        if (stem.Length == 0)
        {
            return fallback;
        }

        var room = Math.Max(1, maxLength - ext.Length);
        if (stem.Length > room)
        {
            var cut = room;
            if (char.IsHighSurrogate(stem[cut - 1]))
            {
                cut--;
            }

            stem = stem[..cut].TrimEnd(' ', '.');
        }

        if (Reserved().IsMatch(stem))
        {
            stem += "_";
        }

        return stem + ext;
    }

    public static string WithExtension(string? title, string extension)
    {
        var stem = Safe(title, "note", MaxLength - extension.Length);
        return stem.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? stem : stem + extension;
    }

    /// <summary>The path in a zip: every directory and the file name made safe, duplicates get " (2)", " (3)" before the extension.</summary>
    public static string UniquePath(string folder, string fileName, HashSet<string> used)
    {
        var parts = folder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p is not ("." or ".."))
            .Select(p => Safe(p, "folder"))
            .ToList();
        var prefix = parts.Count == 0 ? string.Empty : string.Join('/', parts) + "/";
        var name = Safe(fileName);
        var ext = Path.GetExtension(name);
        var stem = name[..(name.Length - ext.Length)];
        var path = prefix + name;
        for (var n = 2; !used.Add(path.ToLowerInvariant()); n++)
        {
            path = prefix + stem + " (" + n + ")" + ext;
        }

        return path;
    }
}
