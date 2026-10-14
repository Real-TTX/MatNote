using System.Security.Cryptography;
using System.Text;

namespace MatNote.Services;

/// <summary>
/// The static files the pages put inline or version: the two icon sprites (the toolkit's and the app's own, which must be in the page for <c>&lt;use href="#..."&gt;</c>)
/// and a hash of the files of the toolkit, for the <c>?v=</c> of their addresses.
/// </summary>
public sealed class Assets
{
    public Assets(IWebHostEnvironment env)
    {
        Sprite = Read(env, "matui/matui-icons.svg") + Read(env, "img/app-icons.svg");
        var parts = new[] { "matui/matui.css", "matui/matui.js", "matui/matui.boot.js", "css/app.css", "js/app.js", "js/editor.js" };
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(parts.Select(p => Read(env, p))))))[..10].ToLowerInvariant();
        IconNames = System.Text.RegularExpressions.Regex.Matches(Sprite, "id=\"mat-i-([a-z0-9-]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
    }

    public string Sprite { get; }

    public string Hash { get; }

    public IReadOnlySet<string> IconNames { get; }

    private static string Read(IWebHostEnvironment env, string path)
    {
        var file = env.WebRootFileProvider.GetFileInfo(path);
        if (!file.Exists)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
