using Flit.Admin.Domain.OtQueries;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, tercera ola) — mismo catálogo de columnas que
/// <c>frontend/components/admin/transit-offices/_reportes/query-columns.ts</c>, portado a C# por la
/// misma razón que <see cref="CompanyQueryReportColumns"/>: el informe de una consulta guardada del
/// organismo lo arma el SCHEDULER, sin navegador que ejecute el escritor de xlsx del cliente.
/// </summary>
internal static class OtQueryReportColumns
{
    /// <summary>Espejo de <c>defaultQueryColumns()</c> (query-columns.ts).</summary>
    private static readonly string[] DefaultColumns =
        ["referencia", "placa", "empresa", "tipo_tramite", "estado", "radicado_en"];

    private static readonly Dictionary<string, string> TransformacionLabel = new(StringComparer.Ordinal)
    {
        ["cambio_color"] = "Color",
        ["cambio_carroceria"] = "Carrocería",
        ["cambio_combustible"] = "Combustible",
    };

    private static readonly Dictionary<string, string> EstadoLabel = new(StringComparer.Ordinal)
    {
        ["en_revision"] = "En revisión",
        ["esperando_placa"] = "Esperando placa",
        ["esperando_cliente"] = "Esperando al cliente",
        ["aprobado"] = "Aprobado",
        ["en_subsanacion"] = "En subsanación",
        ["rechazado"] = "Rechazado",
        ["anulado"] = "Anulado",
        ["otro"] = "Otro",
    };

    private sealed record ColumnDef(string Header, int Width, Func<OtQueryRowDto, TabularWorkbookWriter.Cell> Cell);

    // Anchos calcados de QUERY_COLUMNS (query-columns.ts) — la misma columna debe verse igual de
    // ancha en el export manual y en el adjunto del correo.
    private static readonly Dictionary<string, ColumnDef> Definitions = new(StringComparer.Ordinal)
    {
        ["referencia"] = new("Radicado", 18, r => Text(r.ReferenceNumber)),
        ["placa"] = new("Placa", 12, r => TextOrEmpty(r.Placa)),
        ["vin"] = new("VIN", 20, r => TextOrEmpty(r.Vin)),
        ["empresa"] = new("Empresa", 28, r => Text(r.ClientTenantName)),
        ["tipo_tramite"] = new("Tipo de trámite", 24, r => Text(r.TipoTramite)),
        ["estado"] = new("Estado", 18, r => Text(Label(EstadoLabel, r.EstadoOt))),
        ["prioritario"] = new("Prioritario", 11, r => Text(SiNo(r.Prioritario))),
        ["decidido_por"] = new("Decidido por", 24, r => TextOrEmpty(r.DecididoPor)),
        ["causales"] = new("Causales del último rechazo", 40, r => TextOrEmpty(
            r.CausalesUltimoRechazo.Count == 0 ? null : string.Join(" · ", r.CausalesUltimoRechazo))),
        ["devoluciones"] = new("Devoluciones", 13, r => TabularWorkbookWriter.Cell.Of(r.Devoluciones)),
        ["comprador"] = new("Comprador", 28, r => TextOrEmpty(r.Comprador)),
        ["vendedor"] = new("Vendedor", 28, r => TextOrEmpty(r.Vendedor)),
        ["prenda"] = new("Tiene prenda", 13, r => Text(SiNo(r.TienePrenda))),
        ["acreedor_prenda"] = new("Acreedor de la prenda", 28, r => TextOrEmpty(r.AcreedorPrenda)),
        ["licencia_transito"] = new("LT cargada", 12, r => Text(SiNo(r.TieneLicenciaTransito))),
        ["transformaciones"] = new("Transformaciones", 26, r => TextOrEmpty(r.Transformaciones.Count == 0
            ? null
            : string.Join(" · ", r.Transformaciones.Select(t => Label(TransformacionLabel, t))))),
        ["radicado_en"] = new("Radicado", 14, r => DateCellOrEmpty(r.RadicadoEn)),
        ["decidido_en"] = new("Decidido", 14, r => DateCellOrEmpty(r.DecididoEn)),
        ["aprobado_en"] = new("Aprobado", 14, r => DateCellOrEmpty(r.AprobadoEn)),
        ["actualizado_en"] = new("Última actualización", 18, r => DateTimeCellOrEmpty(r.ActualizadoEn)),
        ["horas_decision"] = new("Tiempo de decisión (h)", 18, r => NumberOrEmpty(r.HorasHastaDecision)),
        ["dias_en_organismo"] = new("Días en el organismo", 18, r => NumberOrEmpty(r.DiasEnOrganismo)),
    };

    /// <summary>
    /// Construye la hoja de un informe de consulta del organismo: solo las columnas de
    /// <paramref name="columnIds"/> que existan en el catálogo, en ese orden — un id desconocido
    /// (columna retirada del producto después de guardar la consulta) se ignora en vez de reventar
    /// el informe completo.
    /// </summary>
    public static TabularWorkbookWriter.Sheet BuildSheet(
        string sheetName, IReadOnlyList<string> columnIds, IReadOnlyList<OtQueryRowDto> rows)
    {
        var ids = columnIds.Count == 0 ? DefaultColumns : columnIds;
        var columns = ids.Where(Definitions.ContainsKey).Select(id => Definitions[id]).ToList();
        if (columns.Count == 0)
            columns = DefaultColumns.Select(id => Definitions[id]).ToList();

        var sheetColumns = columns
            .Select(c => new TabularWorkbookWriter.Column(c.Header, c.Width))
            .ToList();
        var dataRows = rows
            .Select(r => (IReadOnlyList<TabularWorkbookWriter.Cell>)columns.Select(c => c.Cell(r)).ToList())
            .ToList();

        return new TabularWorkbookWriter.Sheet(sheetName, sheetColumns, dataRows);
    }

    private static string Label(Dictionary<string, string> map, string value) =>
        map.TryGetValue(value, out var label) ? label : value;

    private static string SiNo(bool value) => value ? "Sí" : "No";

    private static TabularWorkbookWriter.Cell Text(string value) => TabularWorkbookWriter.Cell.Of(value);

    private static TabularWorkbookWriter.Cell TextOrEmpty(string? value) => TabularWorkbookWriter.Cell.Of(value);

    private static TabularWorkbookWriter.Cell NumberOrEmpty(double? value) =>
        value is null ? TabularWorkbookWriter.Cell.Empty : TabularWorkbookWriter.Cell.Of(value.Value);

    private static TabularWorkbookWriter.Cell DateCellOrEmpty(DateTimeOffset? value)
    {
        if (value is null)
            return TabularWorkbookWriter.Cell.Empty;

        var local = TimeZoneInfo.ConvertTime(value.Value, ScheduleDueEvaluator.BogotaTimeZone);
        return TabularWorkbookWriter.Cell.Of(DateOnly.FromDateTime(local.Date));
    }

    private static TabularWorkbookWriter.Cell DateTimeCellOrEmpty(DateTimeOffset? value)
    {
        if (value is null)
            return TabularWorkbookWriter.Cell.Empty;

        var local = TimeZoneInfo.ConvertTime(value.Value, ScheduleDueEvaluator.BogotaTimeZone);
        return TabularWorkbookWriter.Cell.OfDateTime(DateOnly.FromDateTime(local.Date), local.Hour, local.Minute);
    }
}
