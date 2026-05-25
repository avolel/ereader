using System.Text.RegularExpressions;
using EReader.Core.Exceptions;

namespace EReader.Core.Services;

/// <summary>
/// Validation rules per the auth plan. Kept here (not in the controller) so
/// register/login/update/change-password all enforce the same shape and the
/// rules stay in one place.
/// </summary>
internal static class CredentialValidator
{
    private static readonly Regex UsernamePattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    public static void ValidateUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ValidationException("Username is required.");
        }

        if (username.Length < 3 || username.Length > 32)
        {
            throw new ValidationException("Username must be between 3 and 32 characters.");
        }

        if (!UsernamePattern.IsMatch(username))
        {
            throw new ValidationException("Username may only contain letters, digits, '_' and '-'.");
        }
    }

    public static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ValidationException("Password is required.");
        }

        if (password.Length < 10)
        {
            throw new ValidationException("Password must be at least 10 characters.");
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ValidationException("Password must contain at least one letter and one digit.");
        }
    }
}
