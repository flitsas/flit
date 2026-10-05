using System.Globalization;

namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Lee <c>fur_processing_date</c>. HU13154c: la fecha SOLA (<c>2026-10-01</c>, lo que guarda el formulario) es
/// un día calendario de Colombia y no se desplaza de zona; antes <c>AssumeUniversal</c> la convertía a la hora
/// local del servidor (UTC-5) y el mandato/FUR salían con el día anterior. Con hora explícita no cambia nada.
/// </summary>
public static class FechaTramiteParser
{
    private static readonly string[] FormatosFechaSola = ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy"];

    public static DateTime? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var texto = raw.Trim();
        if (DateTime.TryParseExact(
                texto, FormatosFechaSola, CultureInfo.InvariantCulture, DateTimeStyles.None, out var soloFecha))
        {
            return DateTime.SpecifyKind(soloFecha.Date, DateTimeKind.Unspecified);
        }

        return DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }
}
