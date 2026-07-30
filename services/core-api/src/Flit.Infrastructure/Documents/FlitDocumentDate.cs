using System.Globalization;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Formato de fecha de los documentos que genera FLIT (HU #11049): <b>AÑO/MES/DÍA, sin hora</b>.
///
/// <para>Los certificados imprimían <c>yyyy-MM-dd HH:mm UTC</c> para sus fechas propias, y las fechas de
/// SOAT, RTM y RUES salían tal como las entrega el proveedor —a veces con hora—, así que el consolidado
/// mezclaba formatos y añadía una precisión (hora, minuto) que no aporta a quien revisa el expediente.</para>
///
/// <para>Dos casos distintos, por eso hay dos operaciones:</para>
/// <list type="bullet">
///   <item><see cref="Format(DateTimeOffset)"/> — fechas propias del sistema, ya tipadas.</item>
///   <item><see cref="Normalize"/> — fechas que llegan como TEXTO del proveedor. Si se pueden
///   interpretar se reformatean; si no, <b>se devuelve el original intacto</b>: nunca se inventa ni se
///   vacía un dato de un certificado.</item>
/// </list>
/// </summary>
internal static class FlitDocumentDate
{
    /// <summary>Patrón único de los documentos FLIT.</summary>
    private const string Pattern = "yyyy/MM/dd";

    /// <summary>
    /// Formatos que se aceptan al normalizar texto del proveedor. Se prueban en orden y de forma
    /// EXACTA para no depender de la cultura del runtime (que puede correr en modo
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

    /// <summary>Fecha propia del sistema en el formato documental.</summary>
    internal static string Format(DateTimeOffset value) =>
        value.ToString(Pattern, CultureInfo.InvariantCulture);

    /// <summary>Fecha propia del sistema en el formato documental.</summary>
    internal static string Format(DateTime value) =>
        value.ToString(Pattern, CultureInfo.InvariantCulture);

    /// <summary>
    /// Reformatea a <c>AAAA/MM/DD</c> una fecha que llega como texto (SOAT, RTM, RUES). Devuelve el
    /// valor original —recortado— cuando no se puede interpretar, y <c>null</c>/vacío tal cual.
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
            return Format(exact);
        }

        // Último intento tolerante (ISO 8601 con desplazamiento, sufijos raros del proveedor…). Si
        // tampoco cuadra, el dato se imprime como vino: es información de un certificado externo.
        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var loose)
            ? Format(loose)
            : raw;
    }
}
