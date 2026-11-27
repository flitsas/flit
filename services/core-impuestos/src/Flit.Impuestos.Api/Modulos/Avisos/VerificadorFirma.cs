using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flit.Impuestos.Api.Modulos.Avisos;

/// <summary>Qué dice la verificación de un aviso del proveedor.</summary>
public enum ResultadoFirma
{
    /// <summary>Firma correcta y timestamp dentro de la ventana.</summary>
    Valida,

    /// <summary>Falta <c>X-Flit-Timestamp</c> o <c>X-Flit-Signature</c>.</summary>
    SinCabeceras,

    /// <summary><c>X-Flit-Timestamp</c> no es un entero de segundos Unix.</summary>
    TimestampInvalido,

    /// <summary>El timestamp está a más de <see cref="VerificadorFirma.VentanaSegundos"/> del reloj.</summary>
    FueraDeVentana,

    /// <summary>La firma no coincide.</summary>
    FirmaInvalida,
}

/// <summary>
/// Firma del webhook del proveedor (guía de integración §6, F2b-CF1/CF2): <c>v1=</c> seguido del hex en minúsculas de
/// <c>HMAC-SHA256(webhook_secret, "{X-Flit-Timestamp}.{cuerpo crudo}")</c>. La clave es el secreto completo, con su prefijo
/// <c>whsec_</c>, y el cuerpo es el crudo, tal como llegó. La comparación es en tiempo constante y el timestamp vale a
/// ±<see cref="VentanaSegundos"/> del reloj del servicio. No registra nada: ni el secreto ni el cuerpo salen de aquí.
/// </summary>
/// <example>
/// <code>
/// var r = VerificadorFirma.Verificar(secreto, timestamp, firma, cuerpoCrudo, reloj.GetUtcNow());
/// VerificadorFirma.Firmar("whsec_prueba", "1700000000", "{\"a\":1}"u8); // "v1=0ca0bb9c…"
/// </code>
/// </example>
public static class VerificadorFirma
{
    public const string CabeceraFirma = "X-Flit-Signature";
    public const string CabeceraTimestamp = "X-Flit-Timestamp";
    public const string CabeceraDelivery = "X-Flit-Delivery";
    public const string CabeceraEvento = "X-Flit-Event";

    /// <summary>Único evento que el servicio procesa; otro, con firma válida, se confirma con 200 y se ignora.</summary>
    public const string EventoFinalizada = "consulta.finalizada";
    public const string PrefijoVersion = "v1=";

    /// <summary>Diferencia máxima entre el timestamp del aviso y el reloj (5 minutos, guía §6).</summary>
    public const int VentanaSegundos = 300;

    /// <summary>La firma v1 de <paramref name="cuerpo"/> con <paramref name="timestamp"/> y <paramref name="secreto"/>.</summary>
    public static string Firmar(string secreto, string timestamp, ReadOnlySpan<byte> cuerpo)
    {
        ArgumentException.ThrowIfNullOrEmpty(secreto);
        ArgumentNullException.ThrowIfNull(timestamp);
        return PrefijoVersion + Convert.ToHexStringLower(Hmac(secreto, timestamp, cuerpo));
    }

    public static ResultadoFirma Verificar(string secreto, string? timestamp, string? firma, ReadOnlySpan<byte> cuerpo, DateTimeOffset ahora) =>
        Verificar([secreto], timestamp, firma, cuerpo, ahora);

    /// <summary>
    /// Ventana de rotación: prueba con cada secreto en orden (el vigente primero y luego el anterior) y vale si alguno coincide.
    /// El anterior lo guarda core-impuestos durante 24 h tras un cambio (M07b, <c>webhook_secret_anterior_cifrado</c>).
    /// </summary>
    public static ResultadoFirma Verificar(IReadOnlyList<string> secretos, string? timestamp, string? firma, ReadOnlySpan<byte> cuerpo, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(secretos);
        if (secretos.Count == 0 || secretos.Any(string.IsNullOrEmpty))
            throw new ArgumentException("La verificación exige al menos un secreto no vacío.", nameof(secretos));
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(firma))
            return ResultadoFirma.SinCabeceras;
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos))
            return ResultadoFirma.TimestampInvalido;
        if (Math.Abs(ahora.ToUnixTimeSeconds() - segundos) > VentanaSegundos)
            return ResultadoFirma.FueraDeVentana;

        var recibida = Encoding.UTF8.GetBytes(firma);
        var valida = false;
        foreach (var secreto in secretos)
            valida |= CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Firmar(secreto, timestamp, cuerpo)), recibida);
        return valida ? ResultadoFirma.Valida : ResultadoFirma.FirmaInvalida;
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
