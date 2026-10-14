namespace MatNote.Services;

/// <summary>
/// Where the app keeps its files: one data directory (the Docker volume <c>/data</c>, set with <c>MATNOTE_DATA</c>) with <c>config</c> (the JSON files),
/// <c>keys</c> (the data protection keys, so that sessions and secrets survive a restart) and <c>tmp</c>.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string dataDir)
    {
        DataDir = dataDir;
        Config = Path.Combine(dataDir, "config");
        Keys = Path.Combine(dataDir, "keys");
        Temp = Path.Combine(dataDir, "tmp");
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Keys);
        Directory.CreateDirectory(Temp);
    }

    public string DataDir { get; }

    public string Config { get; }

    public string Keys { get; }

    public string Temp { get; }

    public static string ResolveDataDir()
        => Environment.GetEnvironmentVariable("MATNOTE_DATA") is { Length: > 0 } dir ? dir : "/data";
}
