using System.Globalization;
using Flit.Admin.Application.Plataforma.PersonaNatural;
using Flit.Admin.Domain.Plataforma.PersonaNatural;
using Flit.Queries.Domain.Time;
using Flit.Tramites.Domain.Tramites.Enums;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// HU #14398 (Feature #14396, Épica #14297) — Excel del reporte del portal. Usa el generador del reporte detallado
/// (<see cref="DetailedReportExcelExporter.WriteWorkbookAsync"/>) y las filas de <see cref="IReportePortalReadRepository"/>, la
/// misma consulta del listado: mismo alcance, mismos filtros, mismo orden y las mismas columnas. Modalidad y valores salen
/// vacíos mientras no exista el pago.
/// </summary>
internal sealed class ReportePortalExcelExporter(IReportePortalReadRepository reporte) : IReportePortalExcelExporter
{
    public const string Hoja = "Reporte del portal";

    /// <summary>Mismas columnas y orden que la tabla del reporte.</summary>
    public static readonly string[] Cabeceras =
    [
        "Canal", "Organismo", "Tipo de trámite", "Radicado", "Estado", "Fecha en que cayó",
        "Modalidad de cobro", "Valor del trámite", "Valor de la gestión",
    ];

    public async Task<string?> ExportarAsync(
        Stream destino, AlcanceReportePortal alcance, FiltroReportePortal filtro, (int Page, int PageSize)? pagina, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destino);
        string? error = null;

        await DetailedReportExcelExporter.WriteWorkbookAsync(
            destino,
            Cabeceras,
            async (escribir, token) =>
            {
                error = await reporte.RecorrerAsync(
                    alcance, filtro, pagina,
                    (fila, _) =>
                    {
                        escribir(Celdas(fila));
                        return Task.CompletedTask;
                    },
                    token).ConfigureAwait(false);
            },
            requireRows: false,
            ct,
            Hoja).ConfigureAwait(false);

        return error;
    }

    internal static string[] Celdas(FilaReportePortal f) =>
    [
        TramiteCanal.NombreVisible(f.Canal) ?? f.Canal,
        f.Organismo ?? string.Empty,
        f.Tipo,
        f.Radicado ?? string.Empty,
        f.Estado,
        FormatoFecha.Instante(f.FechaCaida),
        f.Modalidad ?? string.Empty,
        f.ValorTramite?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        f.ValorGestion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
    ];
}
