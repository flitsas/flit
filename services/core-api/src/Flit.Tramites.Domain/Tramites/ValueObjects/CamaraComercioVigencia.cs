namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// HU #12776 — estado de vigencia de un certificado de Cámara de Comercio según su fecha de
/// expedición.
/// </summary>
public enum CamaraComercioVigenciaEstado
{
    /// <summary>
    /// No se pudo saber: el OCR no devolvió fecha, o la devolvió ilegible. <b>No es un problema del
    /// documento</b> y no puede presentarse como si lo fuera.
    /// </summary>
    Indeterminada = 0,

    /// <summary>Expedido dentro de los últimos 30 días.</summary>
    Vigente = 1,

    /// <summary>Expedido hace más de 30 días. Es informativo: no bloquea.</summary>
    Excedida = 2,
}

/// <summary>
/// HU #12776 — resultado de evaluar la vigencia. <see cref="Dias"/> es null cuando no hubo fecha que
/// contar; en los otros dos estados es el número de días transcurridos desde la expedición.
/// </summary>
public readonly record struct CamaraComercioVigenciaResult(
    CamaraComercioVigenciaEstado Estado,
    int? Dias)
{
    /// <summary>¿Hay que mostrarle al gestor la recomendación de actualizar el documento?</summary>
    public bool RequiereAlerta => Estado == CamaraComercioVigenciaEstado.Excedida;
}

/// <summary>
/// HU #12776 (Feature #12773) — ¿el certificado de Cámara de Comercio tiene más de 30 días de
/// expedido?
///
/// <para><b>Informa, no bloquea.</b> Los organismos de tránsito suelen pedir el certificado con no
/// más de 30 días, pero ese plazo lo juzga quien recibe el trámite, no esta plataforma: la señal
/// existe para que el gestor pueda actualizarlo antes de radicar, no para impedírselo. Por eso el
/// estado <see cref="CamaraComercioVigenciaEstado.Excedida"/> no tiene ninguna consecuencia sobre el
/// avance del asistente.</para>
///
/// <para><b>Una fecha ilegible no es un documento vencido.</b> Estos certificados llegan escaneados
/// y el OCR declara su propia legibilidad; cuando no devuelve fecha, el estado es
/// <see cref="CamaraComercioVigenciaEstado.Indeterminada"/> y NO se muestra alerta. Inventar una
/// advertencia sobre un dato que no se tiene entrena al gestor a ignorarlas.</para>
/// </summary>
public static class CamaraComercioVigencia
{
    /// <summary>Días de expedición a partir de los cuales se recomienda actualizar el certificado.</summary>
    public const int DiasMaximos = 30;

    /// <summary>
    /// Evalúa la vigencia contra <paramref name="hoy"/>, que debe venir en día calendario de Colombia
    /// (UTC-5, sin DST — ADR-0025 §3). Se recibe y no se calcula aquí para que la regla siga siendo
    /// pura y comprobable sin reloj.
    /// </summary>
    public static CamaraComercioVigenciaResult Evaluar(DateOnly? fechaExpedicion, DateOnly hoy)
    {
        if (fechaExpedicion is not DateOnly expedicion)
        {
            return new(CamaraComercioVigenciaEstado.Indeterminada, null);
        }

        // Una fecha futura es un error de lectura del OCR, no un certificado del mañana: se cuenta
        // como cero días en vez de producir un negativo que el cliente tendría que interpretar.
        var dias = Math.Max(0, hoy.DayNumber - expedicion.DayNumber);

        // El corte es ESTRICTO: a los 30 días exactos el certificado todavía sirve. Contar el día 30
        // como excedido alertaría sobre documentos que el organismo aún acepta.
        return new(
            dias > DiasMaximos
                ? CamaraComercioVigenciaEstado.Excedida
                : CamaraComercioVigenciaEstado.Vigente,
            dias);
    }

    /// <summary>
    /// Evalúa a partir del texto que dejó el OCR en <c>field_values</c>. El prompt pide la fecha en
    /// <c>YYYY-MM-DD</c>, pero el valor llega sin más normalización que un <c>Trim</c>, así que lo
    /// que no se pueda leer como fecha cae a <see cref="CamaraComercioVigenciaEstado.Indeterminada"/>
    /// — nunca a una fecha adivinada.
    /// </summary>
    public static CamaraComercioVigenciaResult EvaluarTexto(string? fechaExpedicion, DateOnly hoy) =>
        Evaluar(ParseFecha(fechaExpedicion), hoy);

    /// <summary>
    /// Lee la fecha ISO que produce el prompt. Deliberadamente NO acepta formatos ambiguos como
    /// <c>dd/MM/yyyy</c> frente a <c>MM/dd/yyyy</c>: entre alertar de menos y alertar por una fecha
    /// mal interpretada, lo segundo es peor.
    /// </summary>
    public static DateOnly? ParseFecha(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return DateOnly.TryParseExact(
            text, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Nombre estable del estado en el contrato con el cliente.</summary>
    public static string ToWire(CamaraComercioVigenciaEstado estado) => estado switch
    {
        CamaraComercioVigenciaEstado.Vigente => "vigente",
        CamaraComercioVigenciaEstado.Excedida => "excedida",
        _ => "indeterminada",
    };
}
