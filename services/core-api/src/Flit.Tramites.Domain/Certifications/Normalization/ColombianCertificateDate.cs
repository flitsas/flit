using System.Globalization;

namespace Flit.Tramites.Domain.Certifications.Normalization;

/// <summary>
/// Lee la fecha que mandó un proveedor y devuelve el <b>día calendario colombiano</b> que esa fecha
/// representa.
/// </summary>
/// <remarks>
/// Existe por un defecto concreto: normalizar a UTC con <c>AdjustToUniversal</c> puede correr el día
/// impreso en un certificado. Con <c>00:00:00.000-05:00</c> —lo que manda hoy el RUNT— no muerde, pero
/// un proveedor que mande una hora ≥ 19:00 en offset <c>-05:00</c> haría que el documento afirme
/// <b>el día siguiente</b>. En un certificado de vencimiento de SOAT eso no es un detalle de formato.
///
/// <para>Las tres reglas:</para>
/// <list type="number">
///   <item>Si el texto trae offset explícito (o <c>Z</c>), se lleva a <c>-05:00</c> y se toma el día.
///         Un instante UTC se convierte, no se trunca.</item>
///   <item>Si no trae offset, ya <i>es</i> un día colombiano: se toma tal cual, sin convertir nada.</item>
///   <item>Lo que no encaje devuelve <see cref="CertifiedDate.Value"/> = <c>null</c> con el crudo
///         intacto. Nunca se adivina una fecha.</item>
/// </list>
/// </remarks>
public static class ColombianCertificateDate
{
    /// <summary>Offset civil de Colombia. No tiene horario de verano desde 1993, así que es constante.</summary>
    public static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    /// <summary>
    /// Formatos sin zona observados en los proveedores. El orden importa: <c>dd/MM/yyyy</c> va antes
    /// que <c>MM/dd/yyyy</c> —que deliberadamente NO se acepta— porque en Colombia 05/06/2026 es
    /// 5 de junio, y aceptar ambos haría el resultado dependiente del día del mes.
    /// </summary>
    private static readonly string[] LocalFormats =
    [
        "yyyy-MM-dd",
        "dd/MM/yyyy",
        "dd-MM-yyyy",
        "yyyy/MM/dd",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-dd HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss",
    ];

    /// <summary>Rango de cordura: fuera de aquí es basura del proveedor, no una fecha.</summary>
    private const int MinYear = 1900;
    private const int MaxYear = 2200;

    /// <summary>
    /// Normaliza. Devuelve siempre el crudo; <see cref="CertifiedDate.Value"/> queda en <c>null</c>
    /// cuando no se pudo leer (y esa fila entra en <c>normalization_issues</c>).
    /// </summary>
    public static CertifiedDate Parse(string? raw)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
            return CertifiedDate.Empty;

        // 1) Con offset explícito: se convierte al día civil colombiano.
        if (HasExplicitOffset(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var withOffset))
        {
            return Build(DateOnly.FromDateTime(withOffset.ToOffset(ColombiaOffset).DateTime), raw);
        }

        // 2) Sin offset: ya es un día colombiano.
        if (DateTime.TryParseExact(text, LocalFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
        {
            return Build(DateOnly.FromDateTime(local), raw);
        }

        // Última red: cualquier otro formato invariante, interpretado como local (nunca como UTC).
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.NoCurrentDateDefault, out var loose) && loose != default)
        {
            return Build(DateOnly.FromDateTime(loose), raw);
        }

        return new CertifiedDate(null, raw);
    }

    /// <summary>Sobrecarga para proveedores que ya entregan la fecha tipada.</summary>
    public static CertifiedDate FromProvider(DateTimeOffset? value, string? raw = null)
    {
        if (value is null)
            return new CertifiedDate(null, raw);

        var day = DateOnly.FromDateTime(value.Value.ToOffset(ColombiaOffset).DateTime);
        return Build(day, raw ?? value.Value.ToString("O", CultureInfo.InvariantCulture));
    }

    private static CertifiedDate Build(DateOnly day, string? raw) =>
        day.Year is >= MinYear and <= MaxYear
            ? new CertifiedDate(day, raw)
            : new CertifiedDate(null, raw);

    /// <summary>
    /// ¿El texto declara su propia zona? Se busca <c>Z</c> final o un <c>+hh:mm</c>/<c>-hh:mm</c>
    /// después de la parte de fecha — el guion de <c>2026-05-14</c> no cuenta.
    /// </summary>
    private static bool HasExplicitOffset(string text)
    {
        if (text.EndsWith('Z') || text.EndsWith('z'))
            return true;

        var timeStart = text.IndexOf('T');
        if (timeStart < 0)
            timeStart = text.IndexOf(' ');
        if (timeStart < 0)
            return false;

        var tail = text[timeStart..];
        return tail.Contains('+') || tail.Contains('-');
    }
}
