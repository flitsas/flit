using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Ports;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Genera la plantilla XLSX <b>v1</b> que se descarga por <c>GET /lotes/plantilla</c> (CF-11,
/// Feature #12201 I3).
///
/// <para><b>Todas las columnas se emiten con formato TEXTO</b> (<c>numFmtId 49</c>, el <c>@</c> de
/// Excel). No es cosmética: es la mitigación que hace tratable el parser SAX (§8.4). Con formato
/// texto, Excel guarda <c>2026-01-31</c> como cadena y no como serial numérico, y una fecha que
/// igualmente llegue como número se rechaza —no se adivina— en
/// <see cref="StandaloneDocumentXlsxParser"/>.</para>
///
/// <para>Los encabezados salen de <see cref="StandaloneBatchTemplate.Headers"/>, la MISMA lista que
/// valida la carga: la plantilla que se entrega y la que se exige no pueden divergir.</para>
/// </summary>
internal sealed class StandaloneDocumentXlsxTemplate : IStandaloneDocumentXlsxTemplate
{
    private const string Mimetype =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Índice del formato de celda «texto» dentro de la hoja de estilos que se construye abajo.</summary>
    private const uint TextStyleIndex = 1;

    public RenderedStandaloneDocument Build()
    {
        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            var columnas = new Columns(new Column
            {
                Min = 1,
                Max = (uint)StandaloneBatchTemplate.Headers.Count,
                Width = 30,
                CustomWidth = true,
                Style = TextStyleIndex,
            });

            var header = new Row { RowIndex = 1 };
            for (var i = 0; i < StandaloneBatchTemplate.Headers.Count; i++)
            {
                header.Append(TextCell(ColumnName(i) + "1", StandaloneBatchTemplate.Headers[i]));
            }

            worksheetPart.Worksheet = new Worksheet(columnas, new SheetData(header));
            worksheetPart.Worksheet.Save();

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = StandaloneBatchTemplate.SheetName,
            }));

            workbookPart.Workbook.Save();
        }

        return new RenderedStandaloneDocument(
            $"plantilla-generacion-documental-{StandaloneBatchTemplate.Version}.xlsx",
            Mimetype,
            buffer.ToArray());
    }

    /// <summary>
    /// Hoja de estilos mínima con DOS formatos: el 0 general (obligatorio, Excel lo exige) y el 1
    /// con <c>NumberFormatId = 49</c>, que es «Texto».
    /// </summary>
    private static Stylesheet BuildStylesheet() => new(
        new Fonts(new Font()) { Count = 1 },
        new Fills(new Fill(new PatternFill { PatternType = PatternValues.None })) { Count = 1 },
        new Borders(new Border()) { Count = 1 },
        new CellStyleFormats(new CellFormat()) { Count = 1 },
        new CellFormats(
            new CellFormat(),
            new CellFormat { NumberFormatId = 49, ApplyNumberFormat = true })
        {
            Count = 2,
        });

    /// <summary>
    /// Celda de texto EMBEBIDO (<c>inlineStr</c>) en vez de tabla de cadenas compartidas: la
    /// plantilla se lee una vez y así el archivo no necesita un <c>sharedStrings.xml</c> paralelo.
    /// </summary>
    private static Cell TextCell(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        StyleIndex = TextStyleIndex,
        InlineString = new InlineString(new Text(value)),
    };

    /// <summary>Índice 0-based a letras de columna (0 → <c>A</c>, 26 → <c>AA</c>).</summary>
    internal static string ColumnName(int index)
    {
        var nombre = string.Empty;
        var actual = index + 1;

        while (actual > 0)
        {
            var resto = (actual - 1) % 26;
            nombre = ((char)('A' + resto)).ToString(CultureInfo.InvariantCulture) + nombre;
            actual = (actual - 1) / 26;
        }

        return nombre;
    }
}
