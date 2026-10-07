using System.Globalization;
using System.Text;
using Flit.Queries.Domain.Time;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13377 (Épica #13216, diseño §3 «Nombres», CF-13) — nombres de los archivos del lote. Se calculan al
/// empaquetar (PDF y CSV) y al descargar (ZIP): el ZIP almacenado no conoce el total de partes.
/// <list type="bullet">
///   <item>ZIP: <c>consolidados_{yyyyMMdd_HHmm}.zip</c> con una parte; <c>consolidados_{yyyyMMdd_HHmm}_parte-{kk}-de-{tt}.zip</c>
///   con varias. Hora de Colombia (<see cref="ColombiaTime"/>) del <c>created_at</c> del lote.</item>
///   <item>PDF: <c>{radicado}_{PLACA}.pdf</c> o <c>{radicado}_SIN-PLACA.pdf</c>; la placa se sanea a <c>[A-Z0-9]</c>
///   (mayúsculas). El radicado se sanea a <c>[A-Z0-9-]</c> para que el nombre no pueda salir del ZIP
///   (<c>/</c>, <c>\</c>, <c>..</c>).</item>
///   <item>CSV: <see cref="OmitidosCsv"/> en cada parte.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// ConsolidadoLoteNombres.Zip(lote.CreatedAt, parte: 2, totalPartes: 4); // consolidados_20261006_1430_parte-02-de-04.zip
/// ConsolidadoLoteNombres.Pdf("R-2026-1", "abc-12d");                    // R-2026-1_ABC12D.pdf
/// </code>
/// </remarks>
public static class ConsolidadoLoteNombres
{
    /// <summary>Nombre del CSV de omitidos dentro de cada parte.</summary>
    public const string OmitidosCsv = "omitidos.csv";

    /// <summary>Sufijo del PDF cuando el trámite no tiene placa (o no le queda ningún carácter válido).</summary>
    public const string SinPlaca = "SIN-PLACA";

    /// <summary>Radicado de reemplazo si no le queda ningún carácter válido.</summary>
    public const string SinRadicado = "SIN-RADICADO";

    /// <summary>Nombre del ZIP de la parte <paramref name="parte"/> de <paramref name="totalPartes"/>.</summary>
    public static string Zip(DateTimeOffset creadoEn, int parte, int totalPartes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(totalPartes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(parte, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parte, totalPartes);

        var marca = ColombiaTime.From(creadoEn).ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        if (totalPartes == 1)
            return $"consolidados_{marca}.zip";

        var kk = parte.ToString("D2", CultureInfo.InvariantCulture);
        var tt = totalPartes.ToString("D2", CultureInfo.InvariantCulture);
        return $"consolidados_{marca}_parte-{kk}-de-{tt}.zip";
    }

    /// <summary>Nombre del PDF de un ítem incluido.</summary>
    public static string Pdf(string? radicado, string? placa)
    {
        var r = Filtrar(radicado, permitirGuion: true);
        var p = SanearPlaca(placa);
        return $"{(r.Length == 0 ? SinRadicado : r)}_{(p.Length == 0 ? SinPlaca : p)}.pdf";
    }

    /// <summary>Placa en mayúsculas con solo <c>[A-Z0-9]</c>; vacía si no queda nada.</summary>
    public static string SanearPlaca(string? placa) => Filtrar(placa, permitirGuion: false);

    private static string Filtrar(string? valor, bool permitirGuion)
    {
        if (string.IsNullOrEmpty(valor))
            return string.Empty;

        var sb = new StringBuilder(valor.Length);
        foreach (var c in valor.ToUpperInvariant())
        {
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') || (permitirGuion && c == '-'))
                sb.Append(c);
        }

        return sb.ToString();
    }
}
