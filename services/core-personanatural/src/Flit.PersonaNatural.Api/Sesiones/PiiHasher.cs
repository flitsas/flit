using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flit.PersonaNatural.Api.Sesiones;

/// <summary>
/// Huellas HMAC-SHA-256 de datos que se comparan pero no se guardan en claro (cédula, IP). Determinista: la misma cédula
/// da siempre la misma huella con la misma llave, así se compara sin descifrar nada. La llave sale de la configuración
/// (<c>Pii:ClaveHmac</c>) y nunca del repositorio.
/// </summary>
public sealed class PiiHasher
{
    public const string ClaveConfig = "Pii:ClaveHmac";

    private readonly byte[] _clave;

    public PiiHasher(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var texto = configuration[ClaveConfig];
        if (string.IsNullOrWhiteSpace(texto) || texto.Length < 32)
            throw new InvalidOperationException($"{ClaveConfig.Replace(":", "__", StringComparison.Ordinal)} debe tener al menos 32 caracteres.");
        _clave = Encoding.UTF8.GetBytes(texto);
    }

    /// <summary>La cédula se normaliza a solo dígitos y letras en mayúscula: «1.020.456» y «1020456» son la misma.</summary>
    public byte[] HashCedula(string cedula) => Hmac("cedula:" + NormalizarDocumento(cedula));

    public byte[] HashIp(string? ip) => Hmac("ip:" + (ip ?? string.Empty));

    /// <summary>SHA-256 plano (no HMAC): para el token opaco, que ya es aleatorio de 256 bits y no necesita llave.</summary>
    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>Clave de intentos de un radicado: normalizado y con huella, exista o no.</summary>
    public static byte[] ClaveRadicado(string radicado) => SHA256.HashData(Encoding.UTF8.GetBytes("radicado:" + NormalizarRadicado(radicado)));

    public static string NormalizarRadicado(string radicado) => (radicado ?? string.Empty).Trim().ToUpperInvariant();

    public static string NormalizarDocumento(string documento) =>
        new((documento ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Igualdad en tiempo constante entre dos huellas.</summary>
    public static bool Iguales(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

    /// <summary>Token opaco de 256 bits en base64 url-safe, sin relleno.</summary>
    public static string NuevoToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private byte[] Hmac(string texto) => HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes(texto.ToString(CultureInfo.InvariantCulture)));
}
