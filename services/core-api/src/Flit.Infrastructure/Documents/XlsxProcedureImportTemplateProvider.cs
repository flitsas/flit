using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Genera la plantilla Excel (.xlsx) de importación masiva de trámites con el SDK OpenXML (misma
/// dependencia que <see cref="ProcedureExcelExporter"/>). Una hoja "Tramites" con el encabezado y
/// tres filas de ejemplo. Sin dependencias nuevas en el frontend: el navegador solo descarga el binario.
/// </summary>
internal sealed class XlsxProcedureImportTemplateProvider : IProcedureImportTemplateProvider
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Header =
        ["modalidad", "tipo_codigo", "oficina_transito_codigo", "vin", "placa"];

    private static readonly string[][] Examples =
    [
        ["traspaso", "", "", "", "ABC123"],
        ["matricula_inicial", "", "", "9BWZZZ377VT004251", ""],
        ["", "TRASPASO_STANDARD", "", "", "XYZ789"],
    ];

    public byte[] BuildXlsx()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            sheetData.Append(BuildRow(1, Header));
            for (var i = 0; i < Examples.Length; i++)
                sheetData.Append(BuildRow(i + 2, Examples[i]));

            worksheetPart.Worksheet = new Worksheet(sheetData);
            workbookPart.Workbook = new Workbook(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Tramites",
            }));
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Row BuildRow(int rowIndex, string[] values)
    {
        var row = new Row { RowIndex = (uint)rowIndex };
        for (var c = 0; c < values.Length; c++)
        {
            if (values[c].Length == 0)
                continue; // celda vacía → se omite; las referencias A1 mantienen la alineación de columnas.

            row.Append(new Cell
            {
                CellReference = $"{(char)('A' + c)}{rowIndex}",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(values[c])),
            });
        }

        return row;
    }
}
