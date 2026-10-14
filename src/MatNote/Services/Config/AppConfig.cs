using Microsoft.AspNetCore.DataProtection;

namespace MatNote.Services;

/// <summary>The settings of the app that an administrator changes on the System page (<c>config/app.json</c>).</summary>
public sealed class AppConfig
{
    /// <summary>Whether a visitor can create an account on the sign-in page. Off: only an administrator creates accounts.</summary>
    public bool AllowRegistration { get; set; }

    /// <summary>How long a session stays valid when "Stay signed in" is on (it slides: every use extends it).</summary>
    public int SessionDays { get; set; } = 14;

    /// <summary>The largest file a note can hold, in MB.</summary>
    public int MaxUploadMb { get; set; } = 50;

    /// <summary>How many days a deleted note stays in the trash.</summary>
    public int TrashDays { get; set; } = 30;

    /// <summary>The address links of shares are built on (<c>https://notes.example.com</c>); empty: the address of the request.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The time zone of dates on the pages and of the backup schedules; empty: the zone of the container (<c>TZ</c>), or UTC.</summary>
    public string TimeZone { get; set; } = string.Empty;
}

/// <summary>Protects secrets (the key of an authenticator app, the password of a NAS) with the data protection keys, which are kept in the volume.</summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("MatNote.Secrets.v1");

    public string Protect(string plain) => _protector.Protect(plain);

    /// <summary>The plain text, or <c>null</c> when the value cannot be read (other keys).</summary>
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Dates are kept in UTC and shown in the time zone of the app.</summary>
public sealed class Clock(ConfigStore<AppConfig> config)
{
    public TimeZoneInfo Zone
    {
        get
        {
            var name = config.Value.TimeZone;
            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(name);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // fall through to the zone of the container
                }
            }

            return TimeZoneInfo.Local;
        }
    }

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    public string Format(DateTime? utc) => utc is null ? string.Empty : ToLocal(utc.Value).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public string FormatDate(DateTime? utc) => utc is null ? string.Empty : ToLocal(utc.Value).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
