using System.Security.Cryptography;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Token opaco del <c>callbackUrl</c> por empresa (HU #14711, plan §3.3), sin base de datos: base64url sin relleno de
/// <c>tenantId</c> (16 bytes, orden big-endian) seguido de los primeros 16 bytes de <c>HMAC-SHA256(llave, tenantId)</c>. Son 32 bytes,
/// 43 caracteres. El receptor lo lee, recalcula el HMAC y lo compara en tiempo constante: sin la llave del servicio no se puede
/// fabricar uno válido para otra empresa. El token no se registra en logs.
/// </summary>
/// <example>
/// <code>
/// var token = TokenAvisoImpuesto.Emitir(tenantId, llave);
/// TokenAvisoImpuesto.TryLeer(token, llave, out var empresa); // true, empresa == tenantId
/// </code>
/// </example>
internal static class TokenAvisoImpuesto
{
    public const int Largo = 43;
    public const int BytesFirma = 16;

    private const int BytesEmpresa = 16;

    public static string Emitir(Guid tenantId, byte[] llave)
    {
        ArgumentNullException.ThrowIfNull(llave);
        Span<byte> bytes = stackalloc byte[BytesEmpresa + BytesFirma];
        tenantId.TryWriteBytes(bytes[..BytesEmpresa], bigEndian: true, out _);
        Firma(llave, bytes[..BytesEmpresa], bytes[BytesEmpresa..]);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>43 caracteres base64url: uno sin forma no puede ser de nadie y se rechaza sin más trabajo.</summary>
    public static bool TieneForma(string? token) =>
        token is { Length: Largo } && token.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    public static bool TryLeer(string? token, byte[] llave, out Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(llave);
        tenantId = Guid.Empty;
        if (!TieneForma(token))
            return false;
        Span<byte> bytes = stackalloc byte[BytesEmpresa + BytesFirma];
        if (!Convert.TryFromBase64String(token!.Replace('-', '+').Replace('_', '/') + "=", bytes, out var leidos) || leidos != bytes.Length)
            return false;
        Span<byte> esperada = stackalloc byte[BytesFirma];
        Firma(llave, bytes[..BytesEmpresa], esperada);
        if (!CryptographicOperations.FixedTimeEquals(esperada, bytes[BytesEmpresa..]))
            return false;
        // El último carácter base64 lleva 2 bits de relleno: solo vale la forma canónica (otra codificación de los mismos bytes no).
        if (!string.Equals(Emitir(new Guid(bytes[..BytesEmpresa], bigEndian: true), llave), token, StringComparison.Ordinal))
            return false;
        tenantId = new Guid(bytes[..BytesEmpresa], bigEndian: true);
        return true;
    }

    private static void Firma(byte[] llave, ReadOnlySpan<byte> empresa, Span<byte> destino)
    {
        Span<byte> hmac = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(llave, empresa, hmac);
        hmac[..BytesFirma].CopyTo(destino);
    }
}
