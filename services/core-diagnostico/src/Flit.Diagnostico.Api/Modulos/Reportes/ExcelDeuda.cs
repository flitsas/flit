using System.Globalization;
using Flit.Diagnostico.Api.Infraestructura.Excel;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Cell = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Cell;
using Column = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Column;
using Sheet = Flit.Diagnostico.Api.Infraestructura.Excel.TabularWorkbookWriter.Sheet;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Excel del panel de deuda (plan §6.5, HU #14576 AC38) con <see cref="TabularWorkbookWriter"/>: hojas <c>Resumen</c>, <c>SIMIT</c>,
/// <c>Impuesto</c>, <c>SOAT y RTM</c> y <c>Saneamiento</c> (nombres constantes). El de varias placas lleva las mismas hojas con la
/// columna <c>Placa</c> al inicio y, en <c>Resumen</c>, la fila «Omitidos: n» sin ids (SP-9).
/// <list type="bullet">
/// <item>Todo texto de origen externo (comparendo, estado, organismo, nota, motivo, nombre del lote) va con <c>Cell.Of(string)</c>:
/// texto en línea, nunca fórmula; el que parece fórmula queda literal (<see cref="TabularWorkbookWriter.PareceFormula"/>, SP-21).</item>
/// <item>Los valores del SIMIT (comparendos y saldo de acuerdos) van como texto, tal como llegan (S17); los del impuesto, número.</item>
/// <item>El documento de la persona del SIMIT va completo, el mismo <see cref="PersonaDeudaDto.Documento"/> que muestra el panel de
/// deuda (S18, «sin enmascarado adicional»; decisión E9, que revierte SP-11 solo para este Excel). Sin vínculos.</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// var uno = ExcelDeuda.Generar([panel], conPlaca: false);
/// var varios = ExcelDeuda.Generar(paneles, conPlaca: true, omitidos: 2);
/// </code>
/// </example>
internal static class ExcelDeuda
{
    public const string ContentType = TabularWorkbookWriter.ContentType;

    public const string NombreArchivoVarios = "deuda-vehiculos.xlsx";

    public const string HojaResumen = "Resumen";
    public const string HojaSimit = "SIMIT";
    public const string HojaImpuesto = "Impuesto";
    public const string HojaSoatRtm = "SOAT y RTM";
    public const string HojaSaneamiento = "Saneamiento";

    /// <summary><c>deuda-{placa}.xlsx</c> con la placa reducida a <c>[A-Z0-9]</c> (SP-18).</summary>
    public static string NombreArchivo(string placa) => $"deuda-{PdfDeuda.PlacaSegura(placa)}.xlsx";

    public static byte[] Generar(IReadOnlyList<PanelDeudaDto> paneles, bool conPlaca, int omitidos = 0)
    {
        ArgumentNullException.ThrowIfNull(paneles);
        var resumen = new List<IReadOnlyList<Cell>>();
        var simit = new List<IReadOnlyList<Cell>>();
        var impuesto = new List<IReadOnlyList<Cell>>();
        var soatRtm = new List<IReadOnlyList<Cell>>();
        var saneamiento = new List<IReadOnlyList<Cell>>();
        foreach (var panel in paneles)
        {
            IReadOnlyList<Cell> Fila(params Cell[] celdas) => conPlaca ? [Cell.Of(panel.Vehiculo.Placa), .. celdas] : celdas;

            resumen.Add(Fila(Cell.Of("Dictamen"), Cell.Empty, Cell.Of(panel.Dictamen is { } d ? RotulosDiagnostico.DeVeredicto(d.Veredicto) : "Sin dictamen"), Cell.Empty, Cell.Empty));
            foreach (var f in panel.Fuentes)
            {
                resumen.Add(Fila(Cell.Of(f.Fuente), Cell.Of(f.Persona?.Documento), Cell.Of(Estado(f.Estado)), Cell.Of(f.MotivoNa), Fecha(f.ConsultadoEn)));
                foreach (var c in f.Comparendos ?? [])
                    simit.Add(Fila(Cell.Of(f.Fuente), Cell.Of(f.Persona?.Documento), Cell.Of("Comparendo"), Cell.Of(c.Numero), Cell.Of(c.Fecha), Cell.Of(c.Valor), Cell.Of(c.Estado)));
                foreach (var a in f.AcuerdosPago ?? [])
                    simit.Add(Fila(Cell.Of(f.Fuente), Cell.Of(f.Persona?.Documento), Cell.Of("Acuerdo de pago"), Cell.Empty, Cell.Empty, Cell.Of(a.SaldoPendiente), Cell.Of(a.Estado)));
                if (f.Fuente == FuentesDeuda.Impuesto)
                {
                    var monto = f.MontoTotal is { } m ? Cell.Of(m) : Cell.Empty;
                    if (f.Vigencias is not { Count: > 0 } vigencias)
                        impuesto.Add(Fila(Cell.Of(Estado(f.Estado)), monto, Cell.Empty, Cell.Empty, Cell.Empty));
                    else
                    {
                        foreach (var v in vigencias)
                            impuesto.Add(Fila(Cell.Of(Estado(f.Estado)), monto, Cell.Of(v.Anio), Cell.Of(v.Valor), Cell.Of(v.Pagada ? "Sí" : "No")));
                    }
                }

                if (f.Fuente is FuentesDeuda.Soat or FuentesDeuda.Rtm)
                    soatRtm.Add(Fila(Cell.Of(f.Fuente), Cell.Of(Estado(f.Estado)), Cell.Of(f.Vence), Fecha(f.ConsultadoEn)));
            }

            if (panel.Saneamiento is { } s)
            {
                var estado = PdfDeuda.RotulosSaneamiento.GetValueOrDefault(s.Estado, s.Estado);
                if (s.Renglones.Count == 0)
                    saneamiento.Add(Fila(Cell.Of(estado), Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty));
                foreach (var r in s.Renglones)
                    saneamiento.Add(Fila(Cell.Of(estado), Cell.Of(r.Fuente), Cell.Of(r.Hallazgo), Cell.Of(r.Resultado), Cell.Of(r.Nota ?? r.Descripcion), Cell.Of(r.ConEvidencia ? "Sí" : "No")));
            }
            else
            {
                saneamiento.Add(Fila(Cell.Of("Sin saneamiento"), Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty, Cell.Empty));
            }
        }

        if (conPlaca)
            resumen.Add([Cell.Of($"Omitidos: {omitidos.ToString(CultureInfo.InvariantCulture)}")]);

        return TabularWorkbookWriter.Write(
        [
            new Sheet(HojaResumen, Columnas(conPlaca, "Fuente", "Documento", "Estado", "Motivo", "Consultado en"), resumen),
            new Sheet(HojaSimit, Columnas(conPlaca, "Fuente", "Documento", "Tipo", "Número", "Fecha", "Valor", "Estado"), simit),
            new Sheet(HojaImpuesto, Columnas(conPlaca, "Estado", "Monto total", "Año", "Valor", "Pagada"), impuesto),
            new Sheet(HojaSoatRtm, Columnas(conPlaca, "Fuente", "Estado", "Vence", "Consultado en"), soatRtm),
            new Sheet(HojaSaneamiento, Columnas(conPlaca, "Estado", "Fuente", "Hallazgo", "Resultado", "Nota", "Con evidencia"), saneamiento),
        ]);
    }

    private static List<Column> Columnas(bool conPlaca, params string[] nombres) =>
        [.. (conPlaca ? nombres.Prepend("Placa") : nombres).Select(n => new Column(n, n is "Nota" or "Estado" ? 24 : 18))];

    private static string Estado(string codigo) => PdfDeuda.RotulosEstado.GetValueOrDefault(codigo, codigo);

    private static Cell Fecha(DateTimeOffset? instante)
    {
        if (instante is not { } valor)
            return Cell.Empty;
        var local = valor.ToOffset(VigenciaDelDia.OffsetColombia);
        return Cell.OfDateTime(DateOnly.FromDateTime(local.DateTime), local.Hour, local.Minute);
    }
}
