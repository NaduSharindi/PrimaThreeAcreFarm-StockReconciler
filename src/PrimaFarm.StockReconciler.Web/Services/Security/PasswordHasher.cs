using System.Security.Cryptography;

namespace PrimaFarm.StockReconciler.Web.Services.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing (OWASP Password Storage Cheat Sheet: 600,000 iterations).
/// Stored format: PBKDF2-SHA256$iterations$saltBase64$hashBase64
/// </summary>
public static class PasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA256";
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Algorithm}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);   // constant time: no timing side channel
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
