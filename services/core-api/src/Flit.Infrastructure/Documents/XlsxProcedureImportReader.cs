using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Lector de archivos Excel (.xlsx) para la importación masiva de trámites, con el SDK OpenXML
/// (misma dependencia que <see cref="ProcedureExcelExporter"/>, sin paquetes nuevos). Lee la PRIMERA
/// hoja a una grilla de texto (resuelve la tabla de cadenas compartidas y las celdas dispersas por su
/// referencia A1) y delega las reglas de columnas/límite en <see cref="ProcedureImportRowMapper"/>.
/// </summary>
internal sealed class XlsxProcedureImportReader : IProcedureImportFileReader
{
    public ProcedureImportParseOutcome Read(
        Stream stream,
        int maxRows = ProcedureImportRowMapper.DefaultMaxRows)
    {
        ArgumentNullException.ThrowIfNull(stream);

        List<IReadOnlyList<string>> grid;
        try
        {
            using var document = SpreadsheetDocument.Open(stream, false);
            var workbookPart = document.WorkbookPart;
            var sheet = workbookPart?.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault();
            if (workbookPart is null || sheet?.Id?.Value is null)
                return new ProcedureImportParseOutcome([], "El archivo Excel no tiene hojas.");

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            var sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();

            grid = [];
            foreach (var row in sheetData?.Elements<Row>() ?? Enumerable.Empty<Row>())
                grid.Add(ReadRow(row, sharedStrings));
        }
        catch (Exception ex) when (
            ex is OpenXmlPackageException
                or FileFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            return new ProcedureImportParseOutcome([], "No se pudo leer el archivo Excel (.xlsx). Verifica el formato.");
        }

        return ProcedureImportRowMapper.Map(grid, maxRows);
    }

    private static string[] ReadRow(Row row, SharedStringTable? sharedStrings)
    {
        var byColumn = new Dictionary<int, string>();
        var maxColumn = -1;

        foreach (var cell in row.Elements<Cell>())
        {
            var column = ColumnIndex(cell.CellReference?.Value);
            if (column < 0)
                continue;

            byColumn[column] = CellText(cell, sharedStrings);
            if (column > maxColumn)
                maxColumn = column;
        }

        if (maxColumn < 0)
            return [];

        var cells = new string[maxColumn + 1];
        for (var i = 0; i <= maxColumn; i++)
            cells[i] = byColumn.TryGetValue(i, out var value) ? value : string.Empty;
        return cells;
    }

    /// <summary>Convierte la parte alfabética de una referencia A1 (p.ej. "AB12") a índice 0-based.</summary>
    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
            return -1;

        var index = 0;
        foreach (var c in cellReference)
        {
            if (c is >= 'A' and <= 'Z')
                index = (index * 26) + (c - 'A' + 1);
            else if (c is >= 'a' and <= 'z')
                index = (index * 26) + (c - 'a' + 1);
            else
                break;
        }

        return index - 1;
    }

    private static string CellText(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (int.TryParse(cell.CellValue?.InnerText, out var idx) && sharedStrings is not null)
            {
                var item = sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(idx);
                return item?.InnerText ?? string.Empty;
            }

            return string.Empty;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.Text?.Text ?? cell.InnerText;

        return cell.CellValue?.InnerText ?? string.Empty;
    }
}
