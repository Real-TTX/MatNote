using System.Security.Cryptography;
using MatNote.Data;
using Microsoft.AspNetCore.Identity;

namespace MatNote.Services;

/// <summary>Password hashes (PBKDF2 with a salt, the format of ASP.NET Core) and the rules for a new password.</summary>
public sealed class PasswordService
{
    public const int MinLength = 10;

    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string? hash, string password)
        => !string.IsNullOrEmpty(hash) && _hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;

    /// <summary>Something to compare against when a name does not exist, so that the answer takes as long as for a real account.</summary>
    public void SpendTime(string password) => _hasher.VerifyHashedPassword(null!, DummyHash, password);

    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(null!, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    /// <summary>The message for a password that is not allowed, or <c>null</c>.</summary>
    public static string? Check(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"The password needs at least {MinLength} characters.";
        }

        if (password.Length > 200)
        {
            return "The password is too long.";
        }

        if (!string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
        {
            return "The password must not contain the user name.";
        }

        return null;
    }
}
