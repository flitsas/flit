using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Modules.Security.Application.Auth.BulkInvitation;

namespace Flit.Infrastructure.Tests.Documents.BulkInvitations;

/// <summary>Construye .xlsx mínimos para tests del parser de invitación masiva (HU #14422).</summary>
internal static class BulkInvitationXlsxBuilder
{
    public static byte[] WithRows(params (string FullName, string Email, string Role)[] rows)
        => Build(BulkInvitationTemplateCatalog.Headers, rows.Select(r => new[] { r.FullName, r.Email, r.Role }).ToArray());

    public static byte[] WithHeaders(IReadOnlyList<string> headers)
        => Build(headers, []);

    public static byte[] Build(IReadOnlyList<string> headers, IReadOnlyList<string[]> dataRows)
    {
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

            for (var f = 0; f < dataRows.Count; f++)
            {
                var index = (uint)(f + 2);
                var row = new Row { RowIndex = index };
                var values = dataRows[f];
                for (var c = 0; c < values.Length; c++)
                {
                    row.Append(InlineCell(c, index, values[c]));
                }

                sheetData.Append(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();
            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = BulkInvitationTemplateCatalog.SheetName,
            }));
            workbookPart.Workbook.Save();
        }

        return buffer.ToArray();
    }

    private static Cell InlineCell(int column, uint row, string value) => new()
    {
        CellReference = ColumnName(column) + row.ToString(CultureInfo.InvariantCulture),
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value)),
    };

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        var current = index + 1;
        while (current > 0)
        {
            var rem = (current - 1) % 26;
            name = ((char)('A' + rem)).ToString(CultureInfo.InvariantCulture) + name;
            current = (current - 1) / 26;
        }

        return name;
    }
}
