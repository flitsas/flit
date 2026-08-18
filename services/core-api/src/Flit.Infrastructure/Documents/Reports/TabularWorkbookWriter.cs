using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Infrastructure.Documents.Reports;

/// <summary>
/// Escritor genérico de un .xlsx de varias hojas tabulares (Reportes 2.0, HU-D — informes
/// programados de "Uso del aplicativo" y "Organismo de Tránsito", que agregan varias colecciones
/// en vez de una sola tabla de detalle). Mismo patrón de streaming con <see cref="OpenXmlWriter"/>
/// que <see cref="ProcedureExcelExporter"/>, generalizado a N hojas: cada colección del DTO agregado
/// es una hoja con encabezado + filas ya formateadas a texto (el llamador decide el formato de cada
/// celda antes de llegar aquí — este escritor no conoce el dominio).
/// </summary>
internal static class TabularWorkbookWriter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public sealed record Sheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

    public static byte[] Write(IReadOnlyList<Sheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        if (sheets.Count == 0)
            throw new ArgumentException("Se requiere al menos una hoja.", nameof(sheets));

        var tempPath = Path.Combine(Path.GetTempPath(), $"flit-report-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var document = SpreadsheetDocument.Create(tempPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheetsElement = workbookPart.Workbook.AppendChild(new Sheets());

                uint sheetId = 1;
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var sheet in sheets)
                {
                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    using (var writer = OpenXmlWriter.Create(worksheetPart))
                    {
                        writer.WriteStartElement(new Worksheet());
                        writer.WriteStartElement(new SheetData());

                        WriteRow(writer, sheet.Headers);
                        foreach (var row in sheet.Rows)
                            WriteRow(writer, row);

                        writer.WriteEndElement(); // SheetData
                        writer.WriteEndElement(); // Worksheet
                        writer.Close();
                    }

                    sheetsElement.AppendChild(new DocumentFormat.OpenXml.Spreadsheet.Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = sheetId++,
                        Name = UniqueSheetName(sheet.Name, usedNames),
                    });
                }

                workbookPart.Workbook.Save();
            }

            return File.ReadAllBytes(tempPath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static void WriteRow(OpenXmlWriter writer, IReadOnlyList<string> values)
    {
        writer.WriteStartElement(new Row());
        foreach (var value in values)
        {
            var cell = new Cell { DataType = CellValues.InlineString };
            cell.AppendChild(new InlineString(new Text(value ?? string.Empty)));
            writer.WriteElement(cell);
        }

        writer.WriteEndElement();
    }

    /// <summary>Excel exige nombres de hoja únicos, ≤ 31 caracteres y sin <c>: \ / ? * [ ]</c>.</summary>
    private static string UniqueSheetName(string name, HashSet<string> used)
    {
        var sanitized = new string(name.Select(c => "\\/?*[]:".Contains(c) ? '-' : c).ToArray()).Trim();
        if (sanitized.Length == 0)
            sanitized = "Hoja";
        if (sanitized.Length > 31)
            sanitized = sanitized[..31];

        var candidate = sanitized;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var marker = $" ({suffix++})";
            candidate = sanitized[..Math.Min(sanitized.Length, 31 - marker.Length)] + marker;
        }

        return candidate;
    }
}
