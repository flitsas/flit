using System.Globalization;
using Flit.Queries.Domain.Time;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Fechas de los documentos que genera FLIT. Adopta el formato estándar de la plataforma
/// (Épica #12552): <b><c>DD/MM/YYYY HH:mm</c></b> para un instante y <b><c>DD/MM/YYYY</c></b> para
/// una fecha de calendario.
///
/// <para>
/// Esto <b>revierte el criterio de la HU #11049</b>, que imprimía <c>AÑO/MES/DÍA</c> sin hora en
/// todo el consolidado. Es un cambio de criterio del negocio, no la corrección de un defecto:
/// queda anotado en el Discussion de la Épica para que no compute contra aquella historia.
/// </para>
///
/// <para>Siguen siendo dos casos distintos, y ahora la diferencia además se ve:</para>
/// <list type="bullet">
///   <item><see cref="Format(DateTimeOffset)"/> — fechas PROPIAS del sistema, ya tipadas: cuándo se
///   consultó una fuente, cuándo se generó el documento. Son instantes, así que llevan hora y se
///   convierten a la hora de Colombia.</item>
///   <item><see cref="Normalize"/> — fechas que llegan como TEXTO del proveedor (SOAT, RTM, RUES):
///   expedición, vigencia, vencimiento, matrícula. Son fechas de CALENDARIO, así que van sin hora y
///   <b>sin convertir de zona</b> (excepción RN-08): convertirlas correría el día del vencimiento de
///   una póliza. Si no se pueden interpretar <b>se devuelve el original intacto</b>: nunca se
///   inventa ni se vacía un dato de un certificado.</item>
/// </list>
/// </summary>
internal static class FlitDocumentDate
{
    /// <summary>
    /// Formatos que se aceptan al normalizar texto del proveedor. Se prueban en orden y de forma
    /// EXACTA para no depender de la cultura del runtime (que corre en modo
    /// globalization-invariant) y para no confundir día con mes: <c>dd/MM/yyyy</c> va antes que
    /// <c>MM/dd/yyyy</c>, que no se acepta, porque los proveedores colombianos usan día primero.
    /// </summary>
    private static readonly string[] AcceptedFormats =
    [
        "yyyy-MM-ddTHH:mm:ss.fffZ",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd HH:mm",
        "yyyy/MM/dd",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "dd-MM-yyyy HH:mm:ss",
        "dd-MM-yyyy HH:mm",
        "dd-MM-yyyy",
    ];

    /// <summary>Fecha propia del sistema: instante, con hora, en hora de Colombia.</summary>
    internal static string Format(DateTimeOffset value) => FormatoFecha.Instante(value);

    /// <inheritdoc cref="Format(DateTimeOffset)"/>
    internal static string Format(DateTime value) =>
        FormatoFecha.Instante(new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero));

    /// <summary>
    /// Reformatea a <c>DD/MM/YYYY</c> una fecha de calendario que llega como texto (SOAT, RTM,
    /// RUES). Devuelve el valor original —recortado— cuando no se puede interpretar, y
    /// <c>null</c>/vacío tal cual.
    /// </summary>
    internal static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var raw = value.Trim();

        if (DateTimeOffset.TryParseExact(
                raw, AcceptedFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return Calendario(exact);
        }

        // Último intento tolerante (ISO 8601 con desplazamiento, sufijos raros del proveedor…). Si
        // tampoco cuadra, el dato se imprime como vino: es información de un certificado externo.
        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose)
            ? Calendario(loose)
            : raw;
    }

    /// <summary>
    /// Día tal como vino, sin mover la zona. El texto se interpretó con
    /// <see cref="DateTimeStyles.AssumeUniversal"/>, de modo que «2027-01-23» quedó en 00:00Z:
    /// tomar su día en UTC devuelve el 23. Pasarlo a Colombia devolvería el 22 — el corrimiento que
    /// la excepción RN-08 existe para evitar.
    /// </summary>
    private static string Calendario(DateTimeOffset parsed) =>
        FormatoFecha.Calendario(DateOnly.FromDateTime(parsed.UtcDateTime));
}
