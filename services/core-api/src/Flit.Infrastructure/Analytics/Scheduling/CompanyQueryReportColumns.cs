using System.Globalization;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Documents.Reports;

namespace Flit.Infrastructure.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, segunda ola) — mismo catálogo de columnas que
/// <c>frontend/components/atom/modules/_reportes/consultas/company-columns.ts</c>, portado a C#
/// porque el informe de una consulta guardada lo arma el SCHEDULER (sin navegador que ejecute el
/// escritor de xlsx del cliente). Solo el texto formateado (equivalente al <c>value</c> de cada
/// columna del frontend, no el <c>raw</c> tipado): el archivo del correo es de solo lectura, no
/// hace falta que Excel trate una fecha como fecha.
/// </summary>
internal static class CompanyQueryReportColumns
{
    private static readonly CultureInfo Es = CultureInfo.InvariantCulture;

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

    private static readonly Dictionary<string, string> ModalidadLabel = new(StringComparer.Ordinal)
    {
        ["matricula_inicial"] = "Matrícula inicial",
        ["traspaso"] = "Traspaso",
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

    private sealed record ColumnDef(string Header, Func<CompanyQueryRowDto, string> Value);

    private static readonly Dictionary<string, ColumnDef> Definitions = new(StringComparer.Ordinal)
    {
        ["compania"] = new("Compañía", r => r.CompaniaNombre),
        ["referencia"] = new("Radicado", r => r.ReferenceNumber),
        ["placa"] = new("Placa", r => r.Placa ?? "—"),
        ["vin"] = new("VIN", r => r.Vin ?? "—"),
        ["organismo"] = new("Organismo", r => r.TransitOfficeName ?? "—"),
        ["tipo"] = new("Tipo de trámite", r => r.ProcedureTypeName),
        ["modalidad"] = new("Modalidad", r => Label(ModalidadLabel, r.Modalidad)),
        ["estado"] = new("Estado", r => Label(EstadoLabel, r.Status)),
        ["prioritario"] = new("Prioritario", r => SiNo(r.Prioritario)),
        ["radicado_por"] = new("Radicado por", r => r.RadicadoPor),
        ["comprador"] = new("Comprador", r => r.Comprador ?? "—"),
        ["vendedor"] = new("Vendedor", r => r.Vendedor ?? "—"),
        ["prenda"] = new("Prenda", r => SiNo(r.TienePrenda)),
        ["acreedor_prenda"] = new("Acreedor", r => r.AcreedorPrenda ?? "—"),
        ["licencia_transito"] = new("LT cargada", r => SiNo(r.TieneLicenciaTransito)),
        ["transformaciones"] = new("Transformaciones", r => r.Transformaciones.Count == 0
            ? "—"
            : string.Join(", ", r.Transformaciones.Select(t => Label(TransformacionLabel, t)))),
        ["subsanaciones"] = new("Subsanaciones", r => r.SubsanacionCount.ToString(Es)),
        ["leasing"] = new("Leasing", r => SiNo(r.EsLeasing)),
        ["metodo_pago"] = new("Método de pago", r => r.MetodoPago ?? "—"),
        ["tipo_traspaso"] = new("Tipo de traspaso", r => string.IsNullOrEmpty(r.TipoTraspaso)
            ? "—"
            : Label(TraspasoLabel, r.TipoTraspaso)),
        ["creado_en"] = new("Creado", r => FormatDate(r.CreadoEn)),
        ["enviado_en"] = new("Enviado al organismo", r => FormatDateTime(r.EnviadoEn)),
        ["cerrado_en"] = new("Cerrado", r => FormatDateTime(r.CerradoEn)),
        ["aprobado_en"] = new("Aprobado", r => FormatDateTime(r.AprobadoEn)),
        ["actualizado_en"] = new("Última actualización", r => FormatDateTime(r.ActualizadoEn)),
        ["dias_hasta_envio"] = new("Días hasta el envío", r => FormatDays(r.DiasHastaEnvio)),
        ["dias_en_organismo"] = new("Días en el organismo", r => FormatDays(r.DiasEnOrganismo)),
        ["devoluciones"] = new("Devoluciones", r => r.Devoluciones.ToString(Es)),
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

        var headers = columns.Select(c => c.Header).ToList();
        var dataRows = rows
            .Select(r => (IReadOnlyList<string>)columns.Select(c => c.Value(r)).ToList())
            .ToList();

        return new TabularWorkbookWriter.Sheet(sheetName, headers, dataRows);
    }

    private static string Label(Dictionary<string, string> map, string value) =>
        map.TryGetValue(value, out var label) ? label : value;

    private static string SiNo(bool value) => value ? "Sí" : "No";

    private static string FormatDate(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, ScheduleDueEvaluator.BogotaTimeZone).ToString("dd/MM/yyyy", Es);

    private static string FormatDateTime(DateTimeOffset? value) => value is null
        ? "—"
        : TimeZoneInfo.ConvertTime(value.Value, ScheduleDueEvaluator.BogotaTimeZone).ToString("dd/MM/yyyy HH:mm", Es);

    private static string FormatDays(double? value) => value is null
        ? "—"
        : $"{value.Value.ToString("0.#", Es)} {(Math.Abs(value.Value - 1) < 0.001 ? "día" : "días")}";
}
