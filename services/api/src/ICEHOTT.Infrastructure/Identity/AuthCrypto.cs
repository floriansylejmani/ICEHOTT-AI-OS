using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ICEHOTT.Infrastructure.Identity;

public static partial class AuthCrypto
{
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool IsPlausibleToken(string? token) =>
        token is { Length: >= 40 and <= 200 } && token.All(c =>
            c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    public static string NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254)
            throw new ArgumentException("Invalid email.");
        var email = value.Trim().ToLowerInvariant();
        if (email.Length > 254 || !EmailRegex().IsMatch(email))
            throw new ArgumentException("Invalid email.");
        return email;
    }

    public static void ValidatePassword(string? password)
    {
        if (password is null || password.Length < 12 || password.Length > 256 ||
            !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("Password must be 12–256 characters containing letters and numbers.");
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._%+\-]{0,63}@[a-z0-9][a-z0-9.\-]{0,187}\.[a-z]{2,24}$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 150)]
    private static partial Regex EmailRegex();
}
