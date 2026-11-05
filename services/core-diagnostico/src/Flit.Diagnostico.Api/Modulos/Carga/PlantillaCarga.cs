using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// Plantilla de carga masiva (HU #13749, F02-C5, plan §6.1, R-3): un .xlsx con una hoja «Vehiculos» y el encabezado
/// <see cref="ColumnaIdentificador"/> (obligatoria) y las opcionales <see cref="ColumnaTipoDocumento"/> y
/// <see cref="ColumnaDocumento"/>. Las tres columnas van con formato de texto (<c>@</c>) para que Excel no convierta un
/// documento largo en notación científica ni quite ceros. El documento del propietario no se pide: sale del RUNT por placa
/// (D-02). Es fija: igual para toda compañía, se genera una vez y se sirve la misma copia.
/// </summary>
/// <example>
/// <code>
/// var bytes = PlantillaCarga.Generar(); // .xlsx listo para Results.File(bytes, PlantillaCarga.ContentType, PlantillaCarga.NombreArchivo)
/// </code>
/// </example>
internal static class PlantillaCarga
{
    public const string ColumnaIdentificador = "Identificador_Vehiculo";
    public const string ColumnaTipoDocumento = "Tipo_Documento_Locatario";
    public const string ColumnaDocumento = "Documento_Locatario";
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string NombreArchivo = "plantilla-diagnostico.xlsx";

    /// <summary>Formato de número «Texto» predefinido de Excel.</summary>
    private const uint FormatoTexto = 49;

    private static readonly Lazy<byte[]> Copia = new(Construir);

    public static IReadOnlyList<string> Columnas { get; } = [ColumnaIdentificador, ColumnaTipoDocumento, ColumnaDocumento];

    /// <summary>Bytes de la plantilla (copia nueva en cada llamada: quien la recibe puede modificarla).</summary>
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
            var encabezado = new Row { RowIndex = 1 };
            for (var i = 0; i < Columnas.Count; i++)
            {
                encabezado.Append(new Cell
                {
                    CellReference = $"{(char)('A' + i)}1",
                    DataType = CellValues.InlineString,
                    StyleIndex = 2,
                    InlineString = new InlineString(new Text(Columnas[i])),
                });
            }

            hoja.Worksheet = new Worksheet(
                new SheetViews(new SheetView(new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen })
                {
                    WorkbookViewId = 0,
                }),
                new Columns(
                    new Column { Min = 1, Max = 1, Width = 26, CustomWidth = true, Style = 1 },
                    new Column { Min = 2, Max = 2, Width = 28, CustomWidth = true, Style = 1 },
                    new Column { Min = 3, Max = 3, Width = 24, CustomWidth = true, Style = 1 }),
                new SheetData(encabezado));
            partes.Workbook.AppendChild(new Sheets(new Sheet { Id = partes.GetIdOfPart(hoja), SheetId = 1, Name = "Vehiculos" }));
        }

        return flujo.ToArray();
    }

    /// <summary>Estilos mínimos: 0 normal, 1 texto (columnas), 2 texto en negrita (encabezado).</summary>
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
