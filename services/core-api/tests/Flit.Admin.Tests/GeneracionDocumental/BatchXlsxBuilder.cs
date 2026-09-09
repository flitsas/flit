using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Admin.Application.GeneracionDocumental.Batches;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>Cómo se escribe la celda en el XML de la hoja. Es lo que se quiere provocar en cada test.</summary>
internal enum XlsxCellKind
{
    /// <summary>Índice a <c>sharedStrings.xml</c> (<c>t="s"</c>). Es lo que produce Excel al escribir texto.</summary>
    Shared = 0,

    /// <summary>Texto embebido en la celda (<c>t="inlineStr"</c>). Lo producen varios exportadores.</summary>
    Inline = 1,

    /// <summary>Número sin <c>t</c>. Es la forma del serial de fecha que el parser debe RECHAZAR.</summary>
    Number = 2,
}

internal readonly record struct XlsxCell(string Value, XlsxCellKind Kind);

/// <summary>
/// Constructor de XLSX para los tests del parser (HU #12210). Escribe el XML a mano —vía OpenXml
/// crudo, la misma librería del parser— para poder provocar A PROPÓSITO las tres trampas del
/// formato:
///
/// <list type="bullet">
/// <item><b>celdas omitidas:</b> una fila se declara como diccionario índice→celda, así que una
/// columna ausente sencillamente NO se escribe, igual que hace Excel con una casilla vacía;</item>
/// <item><b>shared vs inline:</b> cada celda elige cómo se serializa;</item>
/// <item><b>numérica en columna de fecha:</b> <see cref="XlsxCellKind.Number"/> escribe la celda sin
/// atributo de tipo, que es como Excel guarda una fecha real.</item>
/// </list>
///
/// <para>Ningún dato de estos archivos es real: placas, NIT y cédulas son sintéticos.</para>
/// </summary>
internal static class BatchXlsxBuilder
{
    /// <summary>Índice 0-based de la columna dentro de la plantilla v1, por nombre de encabezado.</summary>
    public static int Col(string header)
    {
        var index = StandaloneBatchTemplate.Headers
            .ToList()
            .FindIndex(h => string.Equals(h, header, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(header), header, "No es una columna de la plantilla v1.");
        }

        return index;
    }

    /// <summary>Fila a partir de pares (encabezado, valor). Todo lo no citado queda VACÍO y omitido.</summary>
    public static Dictionary<int, XlsxCell> Fila(
        XlsxCellKind kind = XlsxCellKind.Shared,
        params (string Header, string Value)[] valores)
    {
        var fila = new Dictionary<int, XlsxCell>();
        foreach (var (header, value) in valores)
        {
            fila[Col(header)] = new XlsxCell(value, kind);
        }

        return fila;
    }

    /// <summary>XLSX con el encabezado canónico de la v1 y las filas indicadas.</summary>
    public static byte[] ConPlantillaV1(params Dictionary<int, XlsxCell>[] filas) =>
        Build([.. StandaloneBatchTemplate.Headers], filas);

    /// <summary>XLSX con un encabezado arbitrario (para el caso <c>template_invalid</c>).</summary>
    public static byte[] Build(IReadOnlyList<string> header, IReadOnlyList<Dictionary<int, XlsxCell>> filas)
    {
        using var buffer = new MemoryStream();
        var shared = new List<string>();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            // Encabezado como shared string: es lo que hace Excel de verdad.
            var headerRow = new Row { RowIndex = 1 };
            for (var i = 0; i < header.Count; i++)
            {
                headerRow.Append(Celda(i, 1, new XlsxCell(header[i], XlsxCellKind.Shared), shared));
            }

            sheetData.Append(headerRow);

            for (var f = 0; f < filas.Count; f++)
            {
                var indice = (uint)(f + 2);
                var row = new Row { RowIndex = indice };

                foreach (var (columna, celda) in filas[f].OrderBy(p => p.Key))
                {
                    row.Append(Celda(columna, indice, celda, shared));
                }

                sheetData.Append(row);
            }

            worksheetPart.Worksheet = new Worksheet(sheetData);
            worksheetPart.Worksheet.Save();

            if (shared.Count > 0)
            {
                var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
                sharedPart.SharedStringTable = new SharedStringTable();
                foreach (var texto in shared)
                {
                    sharedPart.SharedStringTable.Append(new SharedStringItem(new Text(texto)));
                }

                sharedPart.SharedStringTable.Save();
            }

            workbookPart.Workbook.AppendChild(new Sheets(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Hoja1",
            }));

            workbookPart.Workbook.Save();
        }

        return buffer.ToArray();
    }

    private static Cell Celda(int columna, uint fila, XlsxCell celda, List<string> shared)
    {
        var reference = ColumnName(columna) + fila.ToString(System.Globalization.CultureInfo.InvariantCulture);

        switch (celda.Kind)
        {
            case XlsxCellKind.Inline:
                return new Cell
                {
                    CellReference = reference,
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(celda.Value)),
                };

            case XlsxCellKind.Number:
                // Sin DataType: así guarda Excel un número — y una fecha «de verdad», que es un serial.
                return new Cell
                {
                    CellReference = reference,
                    CellValue = new CellValue(celda.Value),
                };

            default:
                var indice = shared.IndexOf(celda.Value);
                if (indice < 0)
                {
                    shared.Add(celda.Value);
                    indice = shared.Count - 1;
                }

                return new Cell
                {
                    CellReference = reference,
                    DataType = CellValues.SharedString,
                    CellValue = new CellValue(indice.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                };
        }
    }

    private static string ColumnName(int index)
    {
        var nombre = string.Empty;
        var actual = index + 1;

        while (actual > 0)
        {
            var resto = (actual - 1) % 26;
            nombre = (char)('A' + resto) + nombre;
            actual = (actual - 1) / 26;
        }

        return nombre;
    }
}
