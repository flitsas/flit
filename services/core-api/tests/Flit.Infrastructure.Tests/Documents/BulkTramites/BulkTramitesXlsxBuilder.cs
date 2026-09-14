using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Tramites.Application.BulkTramites;

namespace Flit.Infrastructure.Tests.Documents.BulkTramites;

/// <summary>Construye XLSX crudos (mismo OpenXml del parser) para los tests de HU #12522.</summary>
internal static class BulkTramitesXlsxBuilder
{
    /// <summary>Archivo con el encabezado exacto del tipo dado y las filas indicadas (pares header/valor).</summary>
    public static byte[] ConPlantilla(
        BulkTramitesTemplateType tipo, params IReadOnlyDictionary<string, string>[] filas)
    {
        var headers = BulkTramitesTemplateCatalog.ColumnsFor(tipo).Select(c => c.Header).ToList();
        return Build(headers, filas);
    }

    public static byte[] ConEncabezado(IReadOnlyList<string> headers) => Build(headers, []);

    private static byte[] Build(IReadOnlyList<string> headers, IReadOnlyDictionary<string, string>[] filas)
    {
        var col = headers.Select((h, i) => (h, i)).ToDictionary(x => x.h, x => x.i, StringComparer.Ordinal);

        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            var headerRow = new Row { RowIndex = 1 };
            for (var i = 0; i < headers.Count; i++)
            {
                headerRow.Append(InlineCell(i, 1, headers[i]));
            }

            sheetData.Append(headerRow);

            for (var f = 0; f < filas.Length; f++)
            {
                var indice = (uint)(f + 2);
                var row = new Row { RowIndex = indice };

                foreach (var (header, valor) in filas[f])
                {
                    row.Append(InlineCell(col[header], indice, valor));
                }

                sheetData.Append(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Datos",
            }));

            workbookPart.Workbook.Save();
        }

        return buffer.ToArray();
    }

    private static Cell InlineCell(int columna, uint fila, string valor) => new()
    {
        CellReference = ColumnName(columna) + fila.ToString(CultureInfo.InvariantCulture),
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(valor)),
    };

    private static string ColumnName(int index)
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
