using MatNote.Data;
using MatNote.Services;

namespace MatNote.Tests;

public class TotpTests
{
    // RFC 6238, appendix B (SHA-1): the secret is the ASCII text "12345678901234567890"; the RFC lists 8 digits, we use the last 6
    private static readonly string Secret = TotpService.Base32Encode(System.Text.Encoding.ASCII.GetBytes("12345678901234567890"));

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Code_matches_the_rfc_6238_test_vectors(long unixSeconds, string expected)
        => Assert.Equal(expected, TotpService.Code(Secret, unixSeconds / TotpService.PeriodSeconds));

    [Fact]
    public void Base32_round_trips()
    {
        var bytes = Enumerable.Range(0, 40).Select(i => (byte)(i * 7)).ToArray();
        Assert.Equal(bytes, TotpService.Base32Decode(TotpService.Base32Encode(bytes)));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Secret);
    }

    [Fact]
    public void Base32_ignores_spaces_dashes_and_case()
        => Assert.Equal(TotpService.Base32Decode(Secret), TotpService.Base32Decode("gezd gnbv-gy3t qojq gezd gnbv gy3t qojq"));

    [Fact]
    public void Match_accepts_one_step_of_drift_and_nothing_more()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = TotpService.Step(now);
        Assert.Equal(step, TotpService.Match(Secret, TotpService.Code(Secret, step), now));
        Assert.Equal(step - 1, TotpService.Match(Secret, TotpService.Code(Secret, step - 1), now));
        Assert.Equal(step + 1, TotpService.Match(Secret, TotpService.Code(Secret, step + 1), now));
        Assert.Null(TotpService.Match(Secret, TotpService.Code(Secret, step + 2), now));
        Assert.Null(TotpService.Match(Secret, "12345", now));
        Assert.Null(TotpService.Match(Secret, "abcdef", now));
    }

    [Fact]
    public void New_secret_is_160_bits_and_different_each_time()
    {
        var a = TotpService.NewSecret();
        Assert.Equal(32, a.Length);
        Assert.NotEqual(a, TotpService.NewSecret());
    }

    [Fact]
    public void Provisioning_uri_has_what_an_authenticator_app_reads()
    {
        var uri = TotpService.ProvisioningUri("MatNotes", "alex@example.com", Secret);
        Assert.StartsWith("otpauth://totp/MatNotes:alex%40example.com?", uri);
        Assert.Contains("secret=" + Secret, uri);
        Assert.Contains("issuer=MatNotes", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public void Qr_code_is_svg_markup_without_inline_styles()
    {
        var svg = TotpService.QrSvg("otpauth://totp/x?secret=ABC");
        Assert.StartsWith("<svg", svg);
        Assert.DoesNotContain("style=", svg);
    }

    [Fact]
    public void Recovery_codes_work_once_and_only_for_the_right_user()
    {
        var (codes, stored) = TotpService.NewRecoveryCodes();
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-Z2-7]{5}-[A-Z2-7]{5}$", c));

        var user = new User { RecoveryCodes = stored };
        Assert.Equal(10, TotpService.RecoveryCodesLeft(user));
        Assert.False(TotpService.UseRecoveryCode(user, "AAAAA-AAAAA"));
        Assert.True(TotpService.UseRecoveryCode(user, codes[3].ToLowerInvariant()));
        Assert.False(TotpService.UseRecoveryCode(user, codes[3]));
        Assert.Equal(9, TotpService.RecoveryCodesLeft(user));
        Assert.False(TotpService.UseRecoveryCode(new User(), codes[0]));
    }

    [Fact]
    public void Recovery_codes_are_stored_as_hashes()
    {
        var (codes, stored) = TotpService.NewRecoveryCodes();
        Assert.All(codes, c => Assert.DoesNotContain(c.Replace("-", string.Empty), stored));
    }
}
