using System.Security.Cryptography;
using System.Text;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Verifica la firma HMAC-SHA256 del webhook de Kyverum (HU #10233, AC2/AC3). Función pura sin
/// dependencias de infraestructura: recibe el cuerpo CRUDO (bytes exactos recibidos, sin re-serializar),
/// la firma del header y el secreto en claro de ESA verificación. Comparación en tiempo constante para
/// evitar timing attacks. Acepta firma en hex (con o sin prefijo <c>sha256=</c>) o base64.
/// </summary>
public static class KyverumWebhookVerifier
{
    /// <summary>Nombre del header que transporta la firma HMAC del webhook (Kyverum: <c>x-kv-signature</c>).</summary>
    public const string SignatureHeader = "x-kv-signature";

    /// <summary>
    /// true si <paramref name="signatureHeader"/> coincide con HMAC-SHA256(<paramref name="rawBody"/>)
    /// usando <paramref name="secret"/>. Cualquier entrada vacía o malformada ⇒ false (no lanza).
    /// </summary>
    public static bool IsValid(byte[]? rawBody, string? signatureHeader, string? secret)
    {
        if (rawBody is null || string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(secret))
            return false;

        var expected = ComputeHmac(rawBody, secret);
        var provided = NormalizeSignature(signatureHeader);
        if (provided.Length == 0)
            return false;

        // Comparación en tiempo constante contra la representación canónica (hex lowercase).
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var providedBytes = Encoding.ASCII.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    /// <summary>HMAC-SHA256(body) en hex lowercase — formato canónico de la firma.</summary>
    public static string ComputeHmac(byte[] rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hmac.ComputeHash(rawBody));
    }

    /// <summary>Normaliza la firma recibida a hex lowercase. Soporta prefijo <c>sha256=</c> y base64.</summary>
    private static string NormalizeSignature(string header)
    {
        var value = header.Trim();
        var eq = value.IndexOf('=');
        if (eq >= 0 && value[..eq].StartsWith("sha", StringComparison.OrdinalIgnoreCase))
            value = value[(eq + 1)..].Trim();

        // ¿hex? (longitud par y solo dígitos hex) ⇒ ya es canónico.
        if (value.Length % 2 == 0 && value.All(Uri.IsHexDigit))
            return value.ToLowerInvariant();

        // ¿base64? ⇒ convertir a hex lowercase.
        try
        {
            return Convert.ToHexStringLower(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}
