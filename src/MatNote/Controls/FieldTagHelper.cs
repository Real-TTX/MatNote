using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatNote.Controls;

/// <summary>One choice of a select, a picker or a group of radios.</summary>
public sealed record Choice(string Value, string Label, string? Hint = null, string? Colour = null);

/// <summary>
/// The one code base of a form field (label above, hint below, the error at the field): <c>&lt;mn-field name="Title" label="Title" required /&gt;</c>.
/// The kinds: text, email, password, number, date, url, tel, search, textarea, select (the native one, for a short list), picker (the toolkit's select with a search),
/// tags, colour, switch, check, otp (a code input). The message of the server (<c>ModelState</c>) is shown at the field, and the id of a field is its name, so the
/// summary at the top of the form can link to it. A field can depend on another (<c>show-if="#Kind"</c> <c>show-value="Link"</c>): it is only shown when it is needed.
/// </summary>
[HtmlTargetElement("mn-field", TagStructure = TagStructure.WithoutEndTag)]
public sealed class FieldTagHelper : TagHelper
{
    [HtmlAttributeName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>The id of the control when it is not the name (two forms of a page with a field of the same name).</summary>
    [HtmlAttributeName("id")] public string? Id { get; set; }

    [HtmlAttributeName("label")] public string? Label { get; set; }

    [HtmlAttributeName("kind")] public string Kind { get; set; } = "text";

    [HtmlAttributeName("value")] public object? Value { get; set; }

    [HtmlAttributeName("required")] public bool Required { get; set; }

    [HtmlAttributeName("optional")] public bool Optional { get; set; }

    [HtmlAttributeName("hint")] public string? Hint { get; set; }

    [HtmlAttributeName("placeholder")] public string? Placeholder { get; set; }

    [HtmlAttributeName("autocomplete")] public string? Autocomplete { get; set; }

    [HtmlAttributeName("inputmode")] public string? InputMode { get; set; }

    [HtmlAttributeName("maxlength")] public int? MaxLength { get; set; }

    [HtmlAttributeName("minlength")] public int? MinLength { get; set; }

    [HtmlAttributeName("rows")] public int Rows { get; set; } = 4;

    [HtmlAttributeName("min")] public string? Min { get; set; }

    [HtmlAttributeName("max")] public string? Max { get; set; }

    [HtmlAttributeName("step")] public string? Step { get; set; }

    [HtmlAttributeName("pattern")] public string? Pattern { get; set; }

    [HtmlAttributeName("options")] public IEnumerable<Choice>? Options { get; set; }

    [HtmlAttributeName("full")] public bool Full { get; set; }

    [HtmlAttributeName("disabled")] public bool Disabled { get; set; }

    [HtmlAttributeName("autofocus")] public bool Autofocus { get; set; }

    [HtmlAttributeName("mono")] public bool Mono { get; set; }

    [HtmlAttributeName("show-if")] public string? ShowIf { get; set; }

    [HtmlAttributeName("show-value")] public string? ShowValue { get; set; }

    [HtmlAttributeName("msg-required")] public string? MsgRequired { get; set; }

    /// <summary>Attributes of the wrapper of a picker (<c>multiple</c>, <c>search</c>, <c>clearable</c>, <c>create-url</c> ...), written as data- attributes.</summary>
    [HtmlAttributeName("picker")] public string? PickerOptions { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        var id = Id ?? Name.Replace('.', '_');
        var value = Convert.ToString(Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        ViewContext.ViewData.ModelState.TryGetValue(Name, out var state);
        var error = state?.Errors.FirstOrDefault()?.ErrorMessage;
        if (state?.AttemptedValue is { } attempted && Kind is not ("password" or "otp"))
        {
            value = attempted;
        }

        var sb = new StringBuilder();
        var cls = "mat-field" + (Full ? " mat-field--full" : string.Empty) + (error is null ? string.Empty : " is-invalid");
        sb.Append($"<div class=\"{cls}\"");
        if (ShowIf is not null)
        {
            sb.Append($" data-show-if=\"{E(ShowIf)}\"");
            if (ShowValue is not null)
            {
                sb.Append($" data-show-value=\"{E(ShowValue)}\"");
            }
        }

        sb.Append('>');

        var describedBy = new List<string>();
        if (Hint is not null)
        {
            describedBy.Add(id + "-hint");
        }

        describedBy.Add(id + "-error");
        var common = new StringBuilder();
        if (Required)
        {
            common.Append(" required");
            common.Append($" data-msg-required=\"{E(MsgRequired ?? "Please fill in this field.")}\"");
        }

        if (Disabled)
        {
            common.Append(" disabled");
        }

        if (Autofocus)
        {
            common.Append(" autofocus");
        }

        if (error is not null)
        {
            common.Append(" aria-invalid=\"true\"");
        }

        common.Append($" aria-describedby=\"{string.Join(' ', describedBy)}\"");

        switch (Kind)
        {
            case "switch":
                var on = value.StartsWith("true", StringComparison.OrdinalIgnoreCase) || value == "on";
                sb.Append($"<label class=\"mat-switch\"><input type=\"checkbox\" id=\"{id}\" name=\"{E(Name)}\" value=\"true\"{(on ? " checked" : string.Empty)}{(Disabled ? " disabled" : string.Empty)} /><span class=\"mat-switch__track\"></span><span>{E(Label)}</span></label>");
                sb.Append($"<input type=\"hidden\" name=\"{E(Name)}\" value=\"false\" />");
                break;
            case "check":
                var ticked = value.StartsWith("true", StringComparison.OrdinalIgnoreCase) || value == "on";
                sb.Append($"<label class=\"mat-check\"><input type=\"checkbox\" id=\"{id}\" name=\"{E(Name)}\" value=\"true\"{(ticked ? " checked" : string.Empty)}{(Disabled ? " disabled" : string.Empty)} /><span class=\"mat-check__text\"><strong>{E(Label)}</strong></span></label>");
                sb.Append($"<input type=\"hidden\" name=\"{E(Name)}\" value=\"false\" />");
                break;
            default:
                if (Label is not null)
                {
                    sb.Append($"<label class=\"mat-label\" for=\"{id}\">{E(Label)}");
                    if (Required)
                    {
                        sb.Append(" <span class=\"mat-req\" aria-hidden=\"true\">*</span>");
                    }
                    else if (Optional)
                    {
                        sb.Append(" <span class=\"mat-opt\">(optional)</span>");
                    }

                    sb.Append("</label>");
                }

                AppendControl(sb, id, value, common.ToString());
                break;
        }

        if (Hint is not null)
        {
            sb.Append($"<span class=\"mat-hint\" id=\"{id}-hint\">{E(Hint)}</span>");
        }

        sb.Append($"<span class=\"mat-error\" id=\"{id}-error\" aria-live=\"polite\">{E(error)}</span>");
        sb.Append("</div>");
        output.Content.SetHtmlContent(sb.ToString());
    }

    private void AppendControl(StringBuilder sb, string id, string value, string common)
    {
        var name = E(Name);
        string Attr(string key, string? v) => v is null ? string.Empty : $" {key}=\"{E(v)}\"";
        var limits = Attr("maxlength", MaxLength?.ToString()) + Attr("minlength", MinLength?.ToString()) + Attr("min", Min) + Attr("max", Max) + Attr("step", Step) + Attr("pattern", Pattern)
                     + Attr("placeholder", Placeholder) + Attr("autocomplete", Autocomplete) + Attr("inputmode", InputMode);

        switch (Kind)
        {
            case "textarea":
                sb.Append($"<textarea class=\"mat-textarea{(Mono ? " mat-mono" : string.Empty)}\" id=\"{id}\" name=\"{name}\" rows=\"{Rows}\"{limits}{common}>{E(value)}</textarea>");
                break;
            case "select":
                sb.Append($"<select class=\"mat-select\" id=\"{id}\" name=\"{name}\"{common}>");
                foreach (var o in Options ?? [])
                {
                    sb.Append($"<option value=\"{E(o.Value)}\"{(o.Value == value ? " selected" : string.Empty)}>{E(o.Label)}</option>");
                }

                sb.Append("</select>");
                break;
            case "picker":
            case "tags":
            case "colour":
                var data = Kind == "picker" ? "select" : Kind;
                sb.Append($"<div class=\"mat-picker\" data-picker=\"{data}\"{Attr("data-placeholder", Placeholder)}");
                foreach (var flag in (PickerOptions ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = flag.IndexOf('=');
                    sb.Append(eq < 0 ? $" data-{E(flag)}" : $" data-{E(flag[..eq])}=\"{E(flag[(eq + 1)..])}\"");
                }

                sb.Append($"><input class=\"mat-picker__input\" id=\"{id}\" name=\"{name}\" value=\"{E(value)}\"{common} />");
                if (Options is not null)
                {
                    sb.Append("<div class=\"mat-picker__options\" hidden>");
                    foreach (var o in Options)
                    {
                        sb.Append($"<button type=\"button\" class=\"mat-picker__option\" data-value=\"{E(o.Value)}\"{(Ui_IsColour(o.Colour) ? $" data-colour=\"{o.Colour}\"" : string.Empty)}>");
                        sb.Append($"<span class=\"mat-picker__text\"><span class=\"mat-picker__name\">{E(o.Label)}</span>");
                        if (o.Hint is not null)
                        {
                            sb.Append($"<span class=\"mat-picker__desc\">{E(o.Hint)}</span>");
                        }

                        sb.Append("</span></button>");
                    }

                    sb.Append("</div>");
                }

                sb.Append("</div>");
                break;
            case "otp":
                sb.Append($"<div class=\"mat-otp mat-otp--lg mat-otp--center\" data-otp data-length=\"6\" data-chars=\"digits\"><input class=\"mat-otp__input\" id=\"{id}\" name=\"{name}\" inputmode=\"numeric\" autocomplete=\"one-time-code\" pattern=\"[0-9]{{6}}\" data-msg-pattern=\"The code has six digits.\"{common} /></div>");
                break;
            default:
                var type = Kind is "email" or "password" or "number" or "date" or "url" or "tel" or "search" ? Kind : "text";
                sb.Append($"<input class=\"mat-input{(Mono ? " mat-mono" : string.Empty)}\" type=\"{type}\" id=\"{id}\" name=\"{name}\" value=\"{E(value)}\"{limits}{common} />");
                break;
        }
    }

    private static bool Ui_IsColour(string? value) => MatNote.Services.Ui.IsColour(value);
}
