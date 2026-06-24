using System.Security.Cryptography;
using System.Text;

namespace Flit.Infrastructure.OtWebhooks;

/// <summary>Hash SHA-256 del secreto webhook y derivación de clave HMAC (HU #10216).</summary>
internal static class OtWebhookSecretHasher
{
    public static string HashSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
    }

    public static byte[] SigningKeyFromStoredHash(string secretHashHex) =>
        Convert.FromHexString(secretHashHex);
}
