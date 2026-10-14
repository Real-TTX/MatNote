using System.Net;
using System.Text;
using MatNote.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace MatNote.Controls;

/// <summary>
/// The one code base of the buttons under a create or edit page, from positive to negative: <c>Save</c>, <c>Back</c>, a space, <c>Delete</c> (FORM-01, LIST-08).
/// <c>&lt;mn-buttons save="Save note" back="/Notes" delete-handler="Delete" delete-confirm="Delete this note?" /&gt;</c>. A button without its attribute is left out.
/// Delete is a submit button of the same form that skips the validation (<c>formnovalidate</c>) and asks first (<c>data-confirm</c>).
/// </summary>
[HtmlTargetElement("mn-buttons", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ButtonRowTagHelper : TagHelper
{
    [HtmlAttributeName("save")] public string? Save { get; set; }

    [HtmlAttributeName("save-icon")] public string SaveIcon { get; set; } = "save";

    [HtmlAttributeName("back")] public string? Back { get; set; }

    [HtmlAttributeName("back-label")] public string BackLabel { get; set; } = "Back";

    [HtmlAttributeName("delete-handler")] public string? DeleteHandler { get; set; }

    [HtmlAttributeName("delete-label")] public string DeleteLabel { get; set; } = "Delete";

    [HtmlAttributeName("delete-confirm")] public string? DeleteConfirm { get; set; }

    [HtmlAttributeName("delete-ok")] public string? DeleteOk { get; set; }

    private static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = null;
        var sb = new StringBuilder("<div class=\"mat-btn-row\">");
        if (Save is not null)
        {
            sb.Append($"<button type=\"submit\" class=\"mat-btn mat-btn--primary\">{Ui.Icon(SaveIcon)}<span>{E(Save)}</span></button>");
        }

        if (Back is not null)
        {
            sb.Append($"<a class=\"mat-btn mat-btn--secondary\" href=\"{E(Back)}\">{Ui.Icon("back")}<span>{E(BackLabel)}</span></a>");
        }

        if (DeleteHandler is not null)
        {
            sb.Append("<span class=\"mat-btn-row__spacer\"></span>");
            sb.Append($"<button type=\"submit\" class=\"mat-btn mat-btn--danger\" formnovalidate formaction=\"?handler={E(DeleteHandler)}\" data-confirm=\"{E(DeleteConfirm ?? "Delete this? This cannot be undone.")}\" data-confirm-ok=\"{E(DeleteOk ?? "Delete")}\">{Ui.Icon("trash")}<span>{E(DeleteLabel)}</span></button>");
        }

        sb.Append("</div>");
        output.Content.SetHtmlContent(sb.ToString());
    }
}
