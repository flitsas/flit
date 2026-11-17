using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Modulos.Backoffice;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Api.Plataforma;
using Cell = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;
using Column = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Column;
using Sheet = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Sheet;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Excel de la bandeja de saneamientos (plan §6.2, HU #14576 AC42) con <see cref="TabularWorkbookWriter"/>: una hoja <c>Saneamientos</c>
/// (nombre constante) con las mismas filas de la bandeja, sin datos personales. Todo texto (nombre del lote, placa) va con
/// <c>Cell.Of(string)</c>: texto en línea, nunca fórmula, y el que parece fórmula queda literal (SP-21). Sin vínculos ni CSV. Fechas en
/// hora Colombia.
/// </summary>
/// <example>
/// <code>
/// var xlsx = ExcelBandejaSaneamientos.Generar(filas);
/// </code>
/// </example>
internal static class ExcelBandejaSaneamientos
{
    public const string ContentType = TabularWorkbookWriter.ContentType;
    public const string NombreArchivo = "saneamientos.xlsx";
    public const string Hoja = "Saneamientos";

    private static readonly Column[] Columnas =
    [
        new("Compañía", 38), new("Diagnóstico", 30), new("Placa", 12), new("Modo", 14), new("Veredicto", 12), new("Estado", 14),
        new("Solicitado", 18), new("Tomado", 18), new("Cerrado", 18), new("Renglones", 10), new("Pendientes", 10),
    ];

    public static byte[] Generar(IReadOnlyList<FilaBandejaDto> filas)
    {
        ArgumentNullException.ThrowIfNull(filas);
        var hoja = new Sheet(Hoja, Columnas, [.. filas.Select(Fila)]);
        return TabularWorkbookWriter.Write([hoja]);
    }

    private static IReadOnlyList<Cell> Fila(FilaBandejaDto f) =>
    [
        Cell.Of(f.Empresa.ToString()), Cell.Of(f.DiagnosticoNombre), Cell.Of(f.Placa), Cell.Of(f.Modo),
        Cell.Of(f.Veredicto is { } v ? RotulosDiagnostico.DeVeredicto(v) : "Sin dictamen"), Cell.Of(f.Estado),
        Fecha(f.SolicitadoEn), Fecha(f.TomadoEn), Fecha(f.CerradoEn), Cell.Of(f.RenglonesTotal), Cell.Of(f.RenglonesPendientes),
    ];

    private static Cell Fecha(DateTimeOffset? instante)
    {
        if (instante is not { } valor)
            return Cell.Empty;
        var local = valor.ToOffset(VigenciaDelDia.OffsetColombia);
        return Cell.OfDateTime(DateOnly.FromDateTime(local.DateTime), local.Hour, local.Minute);
    }
}
