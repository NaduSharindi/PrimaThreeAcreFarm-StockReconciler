using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace PrimaFarm.StockReconciler.Web.Services.Security;

public class ApiClientOptions
{
    /// <summary>Client name -> SHA-256 hex of the API key. Only hashes live in configuration.</summary>
    public Dictionary<string, string> Clients { get; set; } = [];
}

public class ApiKeyValidator(IOptionsMonitor<ApiClientOptions> options)
{
    public const string HeaderName = "X-Api-Key";

    /// <summary>Returns the client name for a valid key, otherwise null.</summary>
    public string? Validate(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 200) return null;
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        string? match = null;
        foreach (var (client, hashHex) in options.CurrentValue.Clients)
        {
            byte[] expected;
            try { expected = Convert.FromHexString(hashHex); } catch (FormatException) { continue; }
            // Compare every entry in constant time; do not short-circuit.
            if (CryptographicOperations.FixedTimeEquals(presented, expected)) match = client;
        }
        return match;
    }

    public static string HashKey(string apiKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
