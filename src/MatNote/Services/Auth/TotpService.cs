using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MatNote.Data;
using QRCoder;

namespace MatNote.Services;

/// <summary>
/// Two-step verification (TOTP, RFC 6238: HMAC-SHA1, 30 seconds, 6 digits: what every authenticator app understands) and the recovery codes.
/// A code is accepted once (<see cref="User.TotpLastStep"/>), one step before and after the current one (clock drift).
/// </summary>
public sealed class TotpService(SecretProtector secrets)
{
    public const int Digits = 6;
    public const int PeriodSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>A new secret (160 bits) as Base32 text, the form an authenticator app takes.</summary>
    public static string NewSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public string Protect(string secret) => secrets.Protect(secret);

    public static long Step(DateTimeOffset time) => time.ToUnixTimeSeconds() / PeriodSeconds;

    /// <summary>The code for a time step (RFC 4226 with the step as counter).</summary>
    public static string Code(string base32Secret, long step)
    {
        var key = Base32Decode(base32Secret);
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % (int)Math.Pow(10, Digits)).ToString(new string('0', Digits), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The step that <paramref name="code"/> belongs to (within one step of <paramref name="now"/>), or <c>null</c>.</summary>
    public static long? Match(string base32Secret, string code, DateTimeOffset now)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length != Digits)
        {
            return null;
        }

        var current = Step(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(base32Secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>Checks a code of the user. On success the step is written to the user (the caller saves): the same code does not work twice.</summary>
    public bool Verify(User user, string code, DateTimeOffset? now = null)
    {
        var secret = secrets.Unprotect(user.TotpSecret);
        if (secret is null)
        {
            return false;
        }

        var step = Match(secret, code, now ?? DateTimeOffset.UtcNow);
        if (step is null || step <= user.TotpLastStep)
        {
            return false;
        }

        user.TotpLastStep = step.Value;
        return true;
    }

    /// <summary>The address an authenticator app reads from the QR code.</summary>
    public static string ProvisioningUri(string issuer, string account, string base32Secret)
    {
        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account);
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
    }

    /// <summary>The QR code as SVG markup (all drawn with attributes, no style).</summary>
    public static string QrSvg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(5, "#000000", "#ffffff", drawQuietZones: true, SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    // ---- recovery codes: ten one-time codes for the day the phone is lost ----

    /// <summary>Makes new recovery codes: the plain codes (shown once) and the JSON of their hashes (kept).</summary>
    public static (IReadOnlyList<string> Codes, string Stored) NewRecoveryCodes(int count = 10)
    {
        var codes = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(10);
            var text = Base32Encode(bytes)[..10];
            codes.Add(text[..5] + "-" + text[5..]);
        }

        return (codes, JsonSerializer.Serialize(codes.Select(HashRecoveryCode)));
    }

    /// <summary>Uses a recovery code: when it is one of the user's unused codes it is removed (the caller saves).</summary>
    public static bool UseRecoveryCode(User user, string code)
    {
        if (string.IsNullOrEmpty(user.RecoveryCodes))
        {
            return false;
        }

        var hashes = JsonSerializer.Deserialize<List<string>>(user.RecoveryCodes) ?? [];
        var candidate = HashRecoveryCode(code);
        var match = hashes.FirstOrDefault(h => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(candidate)));
        if (match is null)
        {
            return false;
        }

        hashes.Remove(match);
        user.RecoveryCodes = JsonSerializer.Serialize(hashes);
        return true;
    }

    public static int RecoveryCodesLeft(User user)
        => string.IsNullOrEmpty(user.RecoveryCodes) ? 0 : JsonSerializer.Deserialize<List<string>>(user.RecoveryCodes)?.Count ?? 0;

    private static string HashRecoveryCode(string code)
    {
        var normal = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("MatNote.recovery:" + normal)));
    }

    public static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    public static byte[] Base32Decode(string text)
    {
        var clean = text.Replace(" ", string.Empty).Replace("-", string.Empty).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException("Not a Base32 character: " + c);
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
