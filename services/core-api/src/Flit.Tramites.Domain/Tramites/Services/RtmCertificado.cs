using System.Globalization;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// HU #11136 — <b>cuándo aplica la tabla certificadora de la RTM</b> (revisión técnico-mecánica) en el
/// "Certificado de vigencia SOAT Y RTM" del expediente.
///
/// <para>Regla del negocio: aplica <b>solo en traspaso</b> y <b>solo</b> si el vehículo tiene <b>más de
/// 5 años</b> desde su fecha de matrícula o registro. Hasta ahora la tabla se pintaba en TODO traspaso
/// sin mirar la antigüedad, y el dato necesario —la fecha de matrícula que reporta el RUNT— se
/// almacenaba sin que ningún documento lo consumiera.</para>
///
/// <para>Vive en el dominio, y no en el generador del PDF, porque es una regla de negocio verificable
/// por sí sola: el generador solo decide cómo se pinta, no a quién le aplica.</para>
/// </summary>
public static class RtmCertificado
{
    /// <summary>Llave de <c>field_values</c> con la fecha de matrícula/registro que reporta el RUNT.</summary>
    public const string FieldKeyFechaMatricula = "vehicle_registration_date";

    /// <summary>Antigüedad mínima, en años, para que la revisión técnico-mecánica sea exigible.</summary>
    public const int AntiguedadMinimaAnios = 5;

    /// <summary>
    /// Formatos aceptados al interpretar la fecha, que llega como TEXTO del proveedor. Se prueban en
    /// orden y de forma EXACTA para no depender de la cultura del runtime; <c>dd/MM/yyyy</c> va antes
    /// que cualquier variante con el mes primero porque los proveedores colombianos usan día primero.
    /// </summary>
    private static readonly string[] FormatosAceptados =
    [
        "yyyy-MM-ddTHH:mm:ss.fffzzz",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy",
        "dd-MM-yyyy",
    ];

    /// <summary>
    /// ¿Se incluye la tabla de RTM en el certificado?
    /// </summary>
    /// <param name="esTraspaso">El trámite es un traspaso (la matrícula inicial nunca lleva RTM).</param>
    /// <param name="fechaMatricula">Fecha de matrícula/registro tal como la reporta el RUNT.</param>
    /// <param name="hoy">Momento contra el que se mide la antigüedad: cuándo se genera el documento.</param>
    /// <remarks>
    /// <b>Sin fecha, la tabla se muestra.</b> Es una decisión deliberada de fallo seguro: omitir una
    /// RTM que sí era exigible deja el expediente incompleto ante el organismo de tránsito, mientras
    /// que incluir una de más solo añade información. Y la fecha falta con más frecuencia de lo que
    /// parece — hay proveedores de RUNT que sencillamente no la reportan.
    /// </remarks>
    public static bool Aplica(bool esTraspaso, string? fechaMatricula, DateTimeOffset hoy)
    {
        if (!esTraspaso)
            return false;

        var matricula = Interpretar(fechaMatricula);
        if (matricula is null)
            return true;

        // Se compara por DÍA CALENDARIO, no por instante: si no, el mismo trámite daría un resultado
        // distinto según la hora a la que se genere el documento el día del quinto aniversario. En el
        // aniversario el vehículo cumple exactamente 5 años, así que todavía no los ha superado.
        var cumpleCinco = matricula.Value.UtcDateTime.Date.AddYears(AntiguedadMinimaAnios);
        return cumpleCinco < hoy.UtcDateTime.Date;
    }

    /// <summary>
    /// Interpreta la fecha del proveedor. <c>null</c> si está ausente o no se puede leer — el llamador
    /// decide qué hacer con esa incertidumbre (ver <see cref="Aplica"/>).
    /// </summary>
    public static DateTimeOffset? Interpretar(string? fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
            return null;

        var raw = fecha.Trim();

        if (DateTimeOffset.TryParseExact(
                raw, FormatosAceptados, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exacta))
        {
            return exacta;
        }

        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var libre)
            ? libre
            : null;
    }
}
