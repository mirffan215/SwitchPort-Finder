using System.Security.Cryptography;

namespace SwitchPortFinder.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA256, random 16-byte salt, 210,000 iterations (OWASP 2023 guidance). Format: v1.{iterations}.{salt}.{hash} (base64).</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000, SaltBytes = 16, HashBytes = 32;
    /// <summary>Hash compared when the user does not exist, so "unknown user" costs the same as "wrong password".</summary>
    public static readonly string Dummy = Hash("dummy-password-for-timing-equalisation");

    public static string Hash(string password, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var it) || it < 1) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, it, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    /// <summary>Minimum policy: 12+ characters, not all the same character, not equal to the username.</summary>
    public static string? Policy(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12) return "Password must be at least 12 characters.";
        if (password.Length > 256) return "Password is too long.";
        if (password.Distinct().Count() < 4) return "Password is too simple.";
        if (username is not null && password.Contains(username, StringComparison.OrdinalIgnoreCase)) return "Password must not contain the username.";
        return null;
    }
}
