using Flit.Infrastructure.Documents.Reports;
using Flit.Queries.Domain;
using Flit.Queries.Domain.Time;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Hoja «Firmas por persona» de los Excel de Consultas (gestor y organismo): una fila por persona que debe firmar,
/// con las MISMAS filas (mismo filtro y criterio) que la hoja principal. No calcula firma: usa lo que ya evaluó
/// <c>ITramiteFirmaEvaluator</c> y viene en cada fila (<see cref="FirmaPersonaDto"/>).
/// </summary>
internal static class FirmasPorPersonaSheet
{
    public const string SheetName = "Firmas por persona";

    /// <summary>Datos del trámite que acompañan a cada persona. <c>Personas</c> nulo = la firma no se calculó.</summary>
    public sealed record Fila(
        string Radicado, string? Organismo, string? Compania, string TipoTramite,
        IReadOnlyList<FirmaPersonaDto>? Personas);

    /// <summary>
    /// <c>null</c> si ninguna fila trae la firma calculada (la consulta no pidió firma): no se crea una hoja vacía.
    /// </summary>
    public static TabularWorkbookWriter.Sheet? Build(
        IReadOnlyList<Fila> filas, bool incluirOrganismo, bool incluirCompania)
    {
        if (!filas.Any(f => f.Personas is not null))
            return null;

        var columns = new List<TabularWorkbookWriter.Column> { new("Radicado", 18) };
        if (incluirOrganismo) columns.Add(new("Organismo", 28));
        if (incluirCompania) columns.Add(new("Compañía", 24));
        columns.Add(new("Tipo de trámite", 24));
        columns.Add(new("Rol", 12));
        columns.Add(new("Nombre", 28));
        columns.Add(new("Documento", 20));
        columns.Add(new("Estado de firma", 16));
        columns.Add(new("Fecha de la firma", 18));

        var rows = new List<IReadOnlyList<TabularWorkbookWriter.Cell>>();
        foreach (var fila in filas)
        {
            foreach (var p in fila.Personas ?? [])
            {
                var cells = new List<TabularWorkbookWriter.Cell> { TabularWorkbookWriter.Cell.Of(fila.Radicado) };
                if (incluirOrganismo) cells.Add(TabularWorkbookWriter.Cell.Of(fila.Organismo));
                if (incluirCompania) cells.Add(TabularWorkbookWriter.Cell.Of(fila.Compania));
                cells.Add(TabularWorkbookWriter.Cell.Of(fila.TipoTramite));
                cells.Add(TabularWorkbookWriter.Cell.Of(p.Rol));
                cells.Add(TabularWorkbookWriter.Cell.Of(p.Nombre));
                cells.Add(TabularWorkbookWriter.Cell.Of(Documento(p)));
                cells.Add(TabularWorkbookWriter.Cell.Of(p.Estado));
                cells.Add(FechaCell(p.FechaFirma));
                rows.Add(cells);
            }
        }

        return new TabularWorkbookWriter.Sheet(SheetName, columns, rows);
    }

    private static string? Documento(FirmaPersonaDto p) =>
        string.IsNullOrWhiteSpace(p.Documento)
            ? null
            : string.IsNullOrWhiteSpace(p.TipoDocumento) ? p.Documento : $"{p.TipoDocumento} {p.Documento}";

    private static TabularWorkbookWriter.Cell FechaCell(DateTimeOffset? value)
    {
        if (value is null)
            return TabularWorkbookWriter.Cell.Empty;
        var local = TimeZoneInfo.ConvertTime(value.Value, ColombiaTime.Zone);
        return TabularWorkbookWriter.Cell.OfDateTime(DateOnly.FromDateTime(local.Date), local.Hour, local.Minute);
    }
}
