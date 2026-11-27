using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>Qué dice la verificación de un aviso del proveedor de impuesto.</summary>
internal enum ResultadoFirmaImpuesto
{
    Valida,
    SinCabeceras,
    TimestampInvalido,
    FueraDeVentana,
    FirmaInvalida,
}

/// <summary>
/// Firma del aviso (guía de integración §6): <c>v1=</c> + hex en minúsculas de
/// <c>HMAC-SHA256(webhook_secret, "{X-Flit-Timestamp}.{cuerpo crudo}")</c>, con el secreto completo (prefijo <c>whsec_</c>) como
/// clave. Comparación en tiempo constante y timestamp a ±<see cref="VentanaSegundos"/> del reloj. Mismo cálculo que
/// <c>VerificadorFirma</c> de core-impuestos. No registra nada.
/// </summary>
internal static class VerificadorFirmaImpuesto
{
    public const string CabeceraFirma = "X-Flit-Signature";
    public const string CabeceraTimestamp = "X-Flit-Timestamp";
    public const string CabeceraDelivery = "X-Flit-Delivery";
    public const string CabeceraEvento = "X-Flit-Event";
    public const string EventoFinalizada = "consulta.finalizada";
    public const string PrefijoVersion = "v1=";
    public const int VentanaSegundos = 300;

    public static string Firmar(string secreto, string timestamp, ReadOnlySpan<byte> cuerpo)
    {
        ArgumentException.ThrowIfNullOrEmpty(secreto);
        ArgumentNullException.ThrowIfNull(timestamp);
        return PrefijoVersion + Convert.ToHexStringLower(Hmac(secreto, timestamp, cuerpo));
    }

    /// <summary>Prueba cada secreto en orden (el vigente primero, luego el anterior); vale si alguno coincide.</summary>
    public static ResultadoFirmaImpuesto Verificar(
        IReadOnlyList<string> secretos, string? timestamp, string? firma, ReadOnlySpan<byte> cuerpo, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(secretos);
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(firma))
            return ResultadoFirmaImpuesto.SinCabeceras;
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos))
            return ResultadoFirmaImpuesto.TimestampInvalido;
        if (Math.Abs(ahora.ToUnixTimeSeconds() - segundos) > VentanaSegundos)
            return ResultadoFirmaImpuesto.FueraDeVentana;

        var recibida = Encoding.UTF8.GetBytes(firma);
        var valida = false;
        foreach (var secreto in secretos)
        {
            if (!string.IsNullOrEmpty(secreto))
                valida |= CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Firmar(secreto, timestamp, cuerpo)), recibida);
        }

        return valida ? ResultadoFirmaImpuesto.Valida : ResultadoFirmaImpuesto.FirmaInvalida;
    }

    private static byte[] Hmac(string secreto, string timestamp, ReadOnlySpan<byte> cuerpo)
    {
        var prefijo = Encoding.UTF8.GetBytes(timestamp + ".");
        var mensaje = new byte[prefijo.Length + cuerpo.Length];
        prefijo.CopyTo(mensaje, 0);
        cuerpo.CopyTo(mensaje.AsSpan(prefijo.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), mensaje);
    }
}
