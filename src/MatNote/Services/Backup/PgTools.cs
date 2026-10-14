using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Npgsql;

namespace MatNote.Services.Backup;

/// <summary>The parts of the connection string that <c>pg_dump</c> and <c>pg_restore</c> need. The password travels in the environment (<c>PGPASSWORD</c>), never on the command line.</summary>
public sealed record PgConnection(string Host, int Port, string Database, string Username, string Password, string SslMode)
{
    public static PgConnection Parse(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        var host = (b.Host ?? string.Empty).Split(',')[0].Trim();
        if (host.Length == 0 || string.IsNullOrWhiteSpace(b.Database))
        {
            throw new InvalidOperationException("The connection string of the database has no host or no database.");
        }

        var ssl = b.SslMode switch
        {
            Npgsql.SslMode.Disable => "disable",
            Npgsql.SslMode.Allow => "allow",
            Npgsql.SslMode.Require => "require",
            Npgsql.SslMode.VerifyCA => "verify-ca",
            Npgsql.SslMode.VerifyFull => "verify-full",
            _ => "prefer",
        };
        return new PgConnection(host, b.Port, b.Database!, string.IsNullOrWhiteSpace(b.Username) ? "postgres" : b.Username, b.Password ?? string.Empty, ssl);
    }

    /// <summary>Host, port, user and database as arguments (no password).</summary>
    public IReadOnlyList<string> Arguments => ["--host", Host, "--port", Port.ToString(CultureInfo.InvariantCulture), "--username", Username, "--dbname", Database, "--no-password"];

    public IReadOnlyDictionary<string, string> Environment => new Dictionary<string, string>
    {
        ["PGPASSWORD"] = Password,
        ["PGSSLMODE"] = SslMode,
        ["PGCONNECT_TIMEOUT"] = "20",
    };
}

public sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Runs <c>pg_dump</c> and <c>pg_restore</c> (the <c>postgresql-client</c> of the Docker image) as child processes, with a time limit, without a shell.</summary>
public static class PgTools
{
    private const int MaxOutput = 400_000;

    public static Task<ProcessResult> DumpAsync(PgConnection connection, string file, CancellationToken ct)
        => RunAsync("pg_dump", ["--format=custom", "--no-owner", "--file", file, .. connection.Arguments], connection.Environment, TimeSpan.FromHours(2), ct);

    /// <summary>One transaction: either everything is restored, or nothing of the database changed.</summary>
    public static Task<ProcessResult> RestoreAsync(PgConnection connection, string file, CancellationToken ct)
        => RunAsync("pg_restore", ["--clean", "--if-exists", "--no-owner", "--single-transaction", "--verbose", .. connection.Arguments, file], connection.Environment, TimeSpan.FromHours(2), ct);

    public static async Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, TimeSpan limit, CancellationToken ct)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"{program} could not be started ({ex.Message}). It belongs to the postgresql-client package, which the Docker image of MatNotes contains.", ex);
        }

        process.StandardInput.Close();
        var output = new StringBuilder();
        var gate = new object();
        void Collect(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (gate)
            {
                if (output.Length < MaxOutput)
                {
                    output.AppendLine(line);
                }
            }
        }

        process.OutputDataReceived += (_, e) => Collect(e.Data);
        process.ErrorDataReceived += (_, e) => Collect(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            process.WaitForExit(); // the last lines of output are collected when this returns
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // it ended by itself in the meantime
            }

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"{program} did not finish within {limit.TotalMinutes:0} minutes and was stopped.");
        }

        lock (gate)
        {
            return new ProcessResult(process.ExitCode, output.ToString().Trim());
        }
    }

    /// <summary>The last lines of an output, for a message: the reason is at the end.</summary>
    public static string Tail(string output, int lines = 8)
    {
        var all = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", all.TakeLast(lines));
    }
}
