using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Impuestos.Api.Modulos.Carga;

/// <summary>
/// Plantilla de la carga masiva (HU #14694, M12; <c>GET /liquidaciones/plantilla</c>): un .xlsx con la hoja <see cref="Hoja"/> y el
/// encabezado <see cref="AnalizadorCarga.Columna"/> en negrita, con la columna en formato de texto (<c>@</c>) para que Excel no
/// convierta una placa numérica. Es fija: igual para toda compañía, se genera una vez y se sirve una copia en cada petición.
/// </summary>
/// <example>
/// <code>
/// Results.File(PlantillaCarga.Generar(), PlantillaCarga.ContentType, PlantillaCarga.NombreArchivo);
/// </code>
/// </example>
public static class PlantillaCarga
{
    public const string Hoja = "Placas";
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string NombreArchivo = "plantilla-impuestos.xlsx";

    /// <summary>Formato de número «Texto» predefinido de Excel.</summary>
    private const uint FormatoTexto = 49;

    private static readonly Lazy<byte[]> Copia = new(Construir);

    /// <summary>Bytes de la plantilla (copia nueva en cada llamada).</summary>
    public static byte[] Generar() => (byte[])Copia.Value.Clone();

    private static byte[] Construir()
    {
        using var flujo = new MemoryStream();
        using (var libro = SpreadsheetDocument.Create(flujo, SpreadsheetDocumentType.Workbook))
        {
            var partes = libro.AddWorkbookPart();
            partes.Workbook = new Workbook();
            var estilos = partes.AddNewPart<WorkbookStylesPart>();
            estilos.Stylesheet = Estilos();
            var hoja = partes.AddNewPart<WorksheetPart>();
            var encabezado = new Row(new Cell
            {
                CellReference = "A1",
                DataType = CellValues.InlineString,
                StyleIndex = 2,
                InlineString = new InlineString(new Text(AnalizadorCarga.Columna)),
            })
            {
                RowIndex = 1,
            };
            hoja.Worksheet = new Worksheet(
                new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
                {
                    WorkbookViewId = 0,
                }),
                new Columns(new Column { Min = 1, Max = 1, Width = 18, CustomWidth = true, Style = 1 }),
                new SheetData(encabezado));
            partes.Workbook.AppendChild(new Sheets(new Sheet { Id = partes.GetIdOfPart(hoja), SheetId = 1, Name = Hoja }));
        }

        return flujo.ToArray();
    }

    /// <summary>Estilos mínimos: 0 normal, 1 texto (columna), 2 texto en negrita (encabezado).</summary>
    private static Stylesheet Estilos() => new(
        new Fonts(new Font(), new Font(new Bold())) { Count = 2 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { NumberFormatId = FormatoTexto, ApplyNumberFormat = true },
            new CellFormat { NumberFormatId = FormatoTexto, ApplyNumberFormat = true, FontId = 1, ApplyFont = true })
        {
            Count = 3,
        });
}
