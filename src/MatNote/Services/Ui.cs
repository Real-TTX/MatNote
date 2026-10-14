using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MatNote.Data;
using Microsoft.AspNetCore.Html;

namespace MatNote.Services;

/// <summary>Small helpers for the markup of the toolkit: icons, the logo, names and sizes.</summary>
public static partial class Ui
{
    public const string AppId = "matnote";
    public const string AppName = "MatNotes";

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NameRegex();

    /// <summary>An icon from the page's inline sprite (the toolkit's and the app's own). A name that is in neither fails a test.</summary>
    public static IHtmlContent Icon(string name, string? extraClass = null, string? colour = null)
    {
        if (!NameRegex().IsMatch(name))
        {
            throw new ArgumentException($"'{name}' is not an icon name.", nameof(name));
        }

        var cls = string.IsNullOrEmpty(extraClass) ? "mat-icon" : $"mat-icon {extraClass}";
        // a colour the user chose is data (COLOR-10): the script draws it, never an inline style
        var data = IsColour(colour) ? $" data-colour=\"{colour}\"" : string.Empty;
        return new HtmlString($"<svg class=\"{cls}\"{data} aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-{name}\"></use></svg>");
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColourRegex();

    public static bool IsColour(string? value) => value is not null && ColourRegex().IsMatch(value);

    /// <summary>The logo tile of the app: its glyph on the accent colour.</summary>
    public static IHtmlContent Logo(string size = "md")
        => new HtmlString($"<span class=\"mat-logo mat-logo--{size}\" role=\"img\" aria-label=\"{AppName}\"><svg class=\"mat-icon\" aria-hidden=\"true\" focusable=\"false\"><use href=\"#mat-i-matnote\"></use></svg></span>");

    /// <summary>The first letters of a name, for an avatar.</summary>
    public static string Initials(string name)
    {
        var letters = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return letters.Length == 0 ? "?" : letters;
    }

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        < 1024L * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.#} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB"),
    };

    /// <summary>The name of the type of a note, as the user reads it.</summary>
    public static string TypeName(NoteType type) => type switch
    {
        NoteType.RichText => "Rich text",
        NoteType.PlainText => "Plain text",
        NoteType.Markdown => "Markdown",
        NoteType.Docx => "Word document",
        NoteType.Xlsx => "Excel workbook",
        NoteType.Pdf => "PDF",
        _ => "File",
    };

    public static string TypeIcon(NoteType type) => type switch
    {
        NoteType.RichText => "file-text",
        NoteType.PlainText => "type",
        NoteType.Markdown => "code",
        NoteType.Docx => "file-text",
        NoteType.Xlsx => "table",
        NoteType.Pdf => "book",
        _ => "paperclip",
    };

    /// <summary>The kinds of notes that have text the app can edit (the others are files).</summary>
    public static bool IsText(NoteType type) => type is NoteType.RichText or NoteType.PlainText or NoteType.Markdown;

    public static string HtmlEncode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
}
