using System.Globalization;
using System.Text.RegularExpressions;

namespace MatNote.Services.Backup;

/// <summary>
/// The name of a backup file: <c>matnote_&lt;scheduleId&gt;_20261014-023000Z.zip</c>. The schedule is in the name, so the retention of one schedule never touches the backups of another
/// on the same target, and the time (UTC) makes the names sort. Every name that comes from a page (download, delete, restore) is checked against this pattern: it
/// has no path separator and no dot-dot, so it cannot point outside the folder of the target.
/// </summary>
public static partial class BackupFileName
{
    /// <summary>The schedule id of the safety backup that a restore makes of the current data before it replaces them.</summary>
    public const string PreRestoreId = "prerestore";

    [GeneratedRegex(@"^matnote_(?<id>[a-z0-9]{3,32})_(?<ts>\d{8}-\d{6})Z\.zip\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static string Create(string scheduleId, DateTime utc)
    {
        if (!IsScheduleId(scheduleId))
        {
            throw new ArgumentException("The schedule id has to be 3 to 32 lower-case letters or digits.", nameof(scheduleId));
        }

        return $"matnote_{scheduleId}_{utc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}Z.zip";
    }

    public static bool IsScheduleId(string? value) => value is { Length: >= 3 and <= 32 } && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9');

    public static bool IsValid(string? name) => TryParse(name, out _, out _);

    /// <summary>Takes a name apart; <c>false</c> for anything that is not a backup file of this app.</summary>
    public static bool TryParse(string? name, out string scheduleId, out DateTime utc)
    {
        scheduleId = string.Empty;
        utc = default;
        if (name is null)
        {
            return false;
        }

        var match = Pattern().Match(name);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["ts"].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc))
        {
            return false;
        }

        scheduleId = match.Groups["id"].Value;
        return true;
    }
}
