using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, segunda ola) — mismo catálogo de columnas que
/// <c>frontend/components/atom/modules/_reportes/consultas/company-columns.ts</c>, portado a C#
/// porque el informe de una consulta guardada lo arma el SCHEDULER (sin navegador que ejecute el
/// escritor de xlsx del cliente). Anchos de columna y celdas TIPADAS (número/fecha, no solo texto)
/// también replican ese archivo — un número como "12" y una fecha como "15/07/2026" en el Excel del
/// correo tienen que poder sumarse/ordenarse en Excel igual que en el export manual, no llegar como
/// texto plano. Solo las columnas que en el frontend no definen <c>raw</c> (modalidad, estado,
/// prioritario, etc. — valores ya traducidos a etiqueta) se quedan en texto aquí también, porque
/// ahí el propio frontend hace lo mismo: sin <c>raw</c>, el Excel manual recibe el mismo texto que
/// la pantalla.
/// </summary>
internal static class CompanyQueryReportColumns
{
    /// <summary>Espejo de <c>defaultCompanyQueryColumns()</c>: lo que se ve si una consulta guardada
    /// quedó sin columnas elegidas (no debería pasar — el picker siempre escribe algo — pero una
    /// consulta vieja o corrupta no debe producir un Excel sin columnas).</summary>
    private static readonly string[] DefaultColumns =
        ["referencia", "placa", "organismo", "tipo", "estado", "creado_en"];

    private static readonly Dictionary<string, string> EstadoLabel = new(StringComparer.Ordinal)
    {
        ["borrador"] = "Borrador",
        ["preparado"] = "Preparado",
        ["entregado"] = "Entregado",
        ["aprobado"] = "Aprobado",
        ["rechazado"] = "Rechazado",
        ["anulado"] = "Anulado",
    };

    private static readonly Dictionary<string, string> TransformacionLabel = new(StringComparer.Ordinal)
    {
        ["cambio_color"] = "Color",
        ["cambio_carroceria"] = "Carrocería",
        ["cambio_combustible"] = "Combustible",
    };

    private static readonly Dictionary<string, string> TraspasoLabel = new(StringComparer.Ordinal)
    {
        ["transferencia_dominio"] = "Transferencia de dominio",
        ["unilateral"] = "Unilateral",
        ["bilateral"] = "Bilateral",
    };

    private sealed record ColumnDef(string Header, int Width, Func<CompanyQueryRowDto, TabularWorkbookWriter.Cell> Cell);

    // Anchos calcados de COMPANY_QUERY_COLUMNS (company-columns.ts) — la misma columna debe verse
    // igual de ancha en el export manual y en el adjunto del correo.
    private static readonly Dictionary<string, ColumnDef> Definitions = new(StringComparer.Ordinal)
    {
        ["compania"] = new("Compañía", 24, r => Text(r.CompaniaNombre)),
        ["referencia"] = new("Radicado", 18, r => Text(r.ReferenceNumber)),
        ["placa"] = new("Placa", 10, r => TextOrEmpty(r.Placa)),
        ["vin"] = new("VIN", 20, r => TextOrEmpty(r.Vin)),
        ["organismo"] = new("Organismo", 28, r => TextOrEmpty(r.TransitOfficeName)),
        ["tipo"] = new("Tipo de trámite", 24, r => Text(r.ProcedureTypeName)),
        ["estado"] = new("Estado", 14, r => Text(Label(EstadoLabel, r.Status))),
        ["prioritario"] = new("Prioritario", 11, r => Text(SiNo(r.Prioritario))),
        ["radicado_por"] = new("Radicado por", 22, r => Text(r.RadicadoPor)),
        ["comprador"] = new("Comprador", 26, r => TextOrEmpty(r.Comprador)),
        ["vendedor"] = new("Vendedor", 26, r => TextOrEmpty(r.Vendedor)),
        ["prenda"] = new("Prenda", 9, r => Text(SiNo(r.TienePrenda))),
        ["acreedor_prenda"] = new("Acreedor", 24, r => TextOrEmpty(r.AcreedorPrenda)),
        ["licencia_transito"] = new("LT cargada", 12, r => Text(SiNo(r.TieneLicenciaTransito))),
        ["transformaciones"] = new("Transformaciones", 24, r => TextOrEmpty(r.Transformaciones.Count == 0
            ? null
            : string.Join(", ", r.Transformaciones.Select(t => Label(TransformacionLabel, t))))),
        ["subsanaciones"] = new("Subsanaciones", 13, r => TabularWorkbookWriter.Cell.Of(r.SubsanacionCount)),
        ["leasing"] = new("Leasing", 9, r => Text(SiNo(r.EsLeasing))),
        ["metodo_pago"] = new("Método de pago", 18, r => TextOrEmpty(r.MetodoPago)),
        ["tipo_traspaso"] = new("Tipo de traspaso", 22, r => TextOrEmpty(string.IsNullOrEmpty(r.TipoTraspaso)
            ? null
            : Label(TraspasoLabel, r.TipoTraspaso))),
        ["creado_en"] = new("Creado", 12, r => DateCell(r.CreadoEn)),
        ["enviado_en"] = new("Enviado al organismo", 18, r => DateTimeCellOrEmpty(r.EnviadoEn)),
        ["cerrado_en"] = new("Cerrado", 18, r => DateTimeCellOrEmpty(r.CerradoEn)),
        ["aprobado_en"] = new("Aprobado", 18, r => DateTimeCellOrEmpty(r.AprobadoEn)),
        ["actualizado_en"] = new("Última actualización", 18, r => DateTimeCellOrEmpty(r.ActualizadoEn)),
        ["dias_hasta_envio"] = new("Días hasta el envío", 16, r => NumberOrEmpty(r.DiasHastaEnvio)),
        ["dias_en_organismo"] = new("Días en el organismo", 17, r => NumberOrEmpty(r.DiasEnOrganismo)),
        ["devoluciones"] = new("Devoluciones", 12, r => TabularWorkbookWriter.Cell.Of(r.Devoluciones)),
    };

    /// <summary>
    /// Construye la hoja de un informe de consulta: solo las columnas de <paramref name="columnIds"/>
    /// que existan en el catálogo, en ese orden — un id desconocido (columna retirada del producto
    /// después de guardar la consulta) se ignora en vez de reventar el informe completo.
    /// </summary>
    public static TabularWorkbookWriter.Sheet BuildSheet(
        string sheetName, IReadOnlyList<string> columnIds, IReadOnlyList<CompanyQueryRowDto> rows)
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

    private static TabularWorkbookWriter.Cell DateCell(DateTimeOffset value)
    {
        var local = TimeZoneInfo.ConvertTime(value, ScheduleDueEvaluator.BogotaTimeZone);
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
