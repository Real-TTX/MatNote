using System.Text.RegularExpressions;
using MatNote.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace MatNote.Services;

/// <summary>Creating and checking the data of an account; the rules are in one place for the setup, the registration and the user management.</summary>
public sealed partial class UserService(AppDbContext db, PasswordService passwords)
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._@-]{1,62}$")]
    private static partial Regex UsernameRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    /// <summary>Checks name, display name and e-mail; adds a message to <paramref name="state"/> for each problem. <paramref name="exceptId"/> is the account being edited.</summary>
    public async Task ValidateIdentityAsync(ModelStateDictionary state, string username, string displayName, string? email, long? exceptId = null)
    {
        var name = LoginService.Normalize(username);
        if (!UsernameRegex().IsMatch(name))
        {
            state.AddModelError("Username", "Use 2 to 63 letters, digits, dots, dashes or underscores.");
        }
        else if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == name && u.Id != exceptId))
        {
            state.AddModelError("Username", "This user name is taken.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            state.AddModelError("DisplayName", "Please enter a name.");
        }
        else if (displayName.Trim().Length > 120)
        {
            state.AddModelError("DisplayName", "The name is too long.");
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var mail = email.Trim().ToLowerInvariant();
            if (!EmailRegex().IsMatch(mail) || mail.Length > 254)
            {
                state.AddModelError("Email", "Please enter an address like name@example.com.");
            }
            else if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email != null && u.Email.ToLower() == mail && u.Id != exceptId))
            {
                state.AddModelError("Email", "This e-mail address is used by another account.");
            }
        }
    }

    /// <summary>Checks a new password and its repetition.</summary>
    public static void ValidatePassword(ModelStateDictionary state, string? password, string? repeat, string? username, string field = "Password", string repeatField = "Repeat")
    {
        if (PasswordService.Check(password, username) is { } problem)
        {
            state.AddModelError(field, problem);
        }
        else if (password != repeat)
        {
            state.AddModelError(repeatField, "The two passwords are not the same.");
        }
    }

    public User Create(string username, string displayName, string? email, string password, UserRole role, bool mustChangePassword = false)
    {
        var user = new User
        {
            Username = LoginService.Normalize(username),
            DisplayName = displayName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            PasswordHash = passwords.Hash(password),
            Role = role,
            MustChangePassword = mustChangePassword,
        };
        db.Users.Add(user);
        return user;
    }
}
