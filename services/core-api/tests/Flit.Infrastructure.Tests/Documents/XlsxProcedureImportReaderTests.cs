using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

public sealed class XlsxProcedureImportReaderTests
{
    private static MemoryStream BuildXlsx(params string[][] rows)
    {
        var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            for (var r = 0; r < rows.Length; r++)
            {
                var row = new Row { RowIndex = (uint)(r + 1) };
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (rows[r][c] is null)
                        continue; // celda ausente (columna dispersa)
                    row.Append(new Cell
                    {
                        CellReference = $"{(char)('A' + c)}{r + 1}",
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(rows[r][c])),
                    });
                }
                sheetData.Append(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            workbookPart.Workbook = new Workbook(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Tramites",
            }));
            workbookPart.Workbook.Save();
        }

        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Read_ParsesHeaderAndDataRows()
    {
        using var xlsx = BuildXlsx(
            ["modalidad", "oficina_transito_codigo", "placa"],
            ["traspaso", "05001", "ABC123"],
            ["matricula_inicial", "11001", ""]);

        var outcome = new XlsxProcedureImportReader().Read(xlsx);

        outcome.FatalError.Should().BeNull();
        outcome.Rows.Should().HaveCount(2);
        outcome.Rows[0].Modalidad.Should().Be("traspaso");
        outcome.Rows[0].TransitOfficeCode.Should().Be("05001");
        outcome.Rows[0].Placa.Should().Be("ABC123");
        outcome.Rows[1].Modalidad.Should().Be("matricula_inicial");
        outcome.Rows[1].Placa.Should().BeNull();
    }

    [Fact]
    public void Read_MapsSparseCellsByReference()
    {
        // La fila de datos omite la celda B (columna oficina) → placa debe seguir en su columna C.
        using var xlsx = BuildXlsx(
            ["modalidad", "oficina_transito_codigo", "placa"],
            ["traspaso", null!, "XYZ789"]);

        var outcome = new XlsxProcedureImportReader().Read(xlsx);

        outcome.Rows.Should().HaveCount(1);
        outcome.Rows[0].Modalidad.Should().Be("traspaso");
        outcome.Rows[0].TransitOfficeCode.Should().BeNull();
        outcome.Rows[0].Placa.Should().Be("XYZ789");
    }

    [Fact]
    public void Read_InvalidFile_ReturnsFatalError()
    {
        using var garbage = new MemoryStream("no soy un xlsx"u8.ToArray());

        var outcome = new XlsxProcedureImportReader().Read(garbage);

        outcome.Rows.Should().BeEmpty();
        outcome.FatalError.Should().NotBeNull();
    }
}
