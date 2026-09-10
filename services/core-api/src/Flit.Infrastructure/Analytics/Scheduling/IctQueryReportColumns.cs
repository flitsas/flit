using Flit.Analytics.Application.IctQueries;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, cuarta ola) — mismo catálogo de columnas que
/// <c>frontend/components/atom/modules/_ict/ict-query-columns.ts</c>, portado a C# por la misma
/// razón que <see cref="CompanyQueryReportColumns"/> y <see cref="OtQueryReportColumns"/>: el
/// informe de una consulta guardada de ICT lo arma el SCHEDULER, sin navegador que ejecute el
/// escritor de xlsx del cliente. <c>Estado</c> replica <c>ICT_ESTADO_META</c> (solo la etiqueta —
/// el color no aplica a un Excel).
/// </summary>
internal static class IctQueryReportColumns
{
    /// <summary>Espejo de <c>ICT_QUERY_PRESETS["basico"]</c> (ict-query-columns.ts).</summary>
    private static readonly string[] DefaultColumns =
        ["radicado", "placa", "empresa", "tipo_tramite", "estado", "registrado_en"];

    private static readonly Dictionary<string, string> EstadoLabel = new(StringComparer.Ordinal)
    {
        ["recibido"] = "Recibido",
        ["en_validacion_negocio"] = "En validación de negocio",
        ["en_validacion_externa"] = "En validación externa",
        ["con_novedades"] = "Con novedades",
        ["borrador_creado"] = "Borrador creado",
        ["anulado"] = "Anulado",
    };

    private sealed record ColumnDef(string Header, int Width, Func<IctQueryRowDto, TabularWorkbookWriter.Cell> Cell);

    // Anchos calcados de ICT_QUERY_COLUMNS (ict-query-columns.ts) — la misma columna debe verse
    // igual de ancha en el export manual y en el adjunto del correo.
    private static readonly Dictionary<string, ColumnDef> Definitions = new(StringComparer.Ordinal)
    {
        ["radicado"] = new("Radicado", 18, r => TextOrEmpty(r.Radicado)),
        ["transaccion"] = new("N.º de transacción", 16, r => TabularWorkbookWriter.Cell.Of(r.TransactionNumber)),
        ["placa"] = new("Placa", 12, r => TextOrEmpty(r.Placa)),
        ["vin"] = new("VIN", 20, r => TextOrEmpty(r.Vin)),
        ["empresa"] = new("Empresa", 28, r => Text(r.TenantNombre)),
        ["tipo_tramite"] = new("Tipo de trámite", 18, r => TextOrEmpty(r.TipoTramite)),
        ["estado"] = new("Estado", 20, r => Text(Label(EstadoLabel, r.Estado))),
        ["tiene_novedades"] = new("Tiene novedades", 14, r => Text(SiNo(r.TieneNovedades))),
        ["tiene_borrador"] = new("Borrador creado", 14, r => Text(SiNo(r.TieneBorrador))),
        ["prioritario"] = new("Prioritario", 11, r => Text(SiNo(r.Prioritario))),
        ["comentarios"] = new("Comentarios", 40, r => TextOrEmpty(r.Comentarios)),
        ["secretaria"] = new("Secretaría", 24, r => TextOrEmpty(r.Secretaria)),
        ["cliente_integracion"] = new("Cliente de integración", 24, r => TextOrEmpty(r.ClienteIntegracion)),
        ["registrado_en"] = new("Fecha de registro", 18, r => DateTimeCell(r.RegistradoEn)),
        ["validacion_negocio_en"] = new("Fecha de validación de negocio", 18, r => DateTimeCellOrEmpty(r.ValidacionNegocioEn)),
        ["validacion_externa_en"] = new("Fecha de validación externa", 18, r => DateTimeCellOrEmpty(r.ValidacionExternaEn)),
    };

    /// <summary>
    /// Construye la hoja de un informe de consulta de ICT: solo las columnas de
    /// <paramref name="columnIds"/> que existan en el catálogo, en ese orden — un id desconocido
    /// (columna retirada del producto después de guardar la consulta) se ignora en vez de reventar
    /// el informe completo.
    /// </summary>
    public static TabularWorkbookWriter.Sheet BuildSheet(
        string sheetName, IReadOnlyList<string> columnIds, IReadOnlyList<IctQueryRowDto> rows)
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

    private static TabularWorkbookWriter.Cell DateTimeCell(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, ScheduleDueEvaluator.BogotaTimeZone);
        return TabularWorkbookWriter.Cell.OfDateTime(DateOnly.FromDateTime(local.Date), local.Hour, local.Minute);
    }

    private static TabularWorkbookWriter.Cell DateTimeCellOrEmpty(DateTimeOffset? value)
    {
        if (value is null)
            return TabularWorkbookWriter.Cell.Empty;

        return DateTimeCell(value.Value);
    }
}
