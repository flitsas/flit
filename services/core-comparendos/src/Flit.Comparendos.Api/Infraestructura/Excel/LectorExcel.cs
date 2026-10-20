using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Comparendos.Api.Infraestructura.Excel;

/// <summary>Una celda leída: el texto tal como se ve y, si Excel la guardó como número, su valor numérico.</summary>
public sealed record CeldaLeida(string Texto, double? Numero);

/// <summary>
/// Lee la primera hoja de un .xlsx (cargas masivas pequeñas, hasta unos miles de filas). Resuelve la columna por la
/// referencia de cada celda porque Excel omite las celdas vacías (mismo cuidado que BulkTramitesXlsxParser de core-api),
/// y entiende textos compartidos, en línea y resultados de fórmulas.
/// </summary>
public static class LectorExcel
{
    public static List<Dictionary<int, CeldaLeida>> LeerPrimeraHoja(Stream xlsx)
    {
        using var doc = SpreadsheetDocument.Open(xlsx, false);
        var libro = doc.WorkbookPart ?? throw new InvalidDataException("El archivo no es un libro de Excel válido.");
        var hoja = libro.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault() ?? throw new InvalidDataException("El libro no tiene hojas.");
        var parte = (WorksheetPart)libro.GetPartById(hoja.Id!);
        var compartidos = libro.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];

        var filas = new List<Dictionary<int, CeldaLeida>>();
        foreach (var fila in parte.Worksheet.Descendants<Row>())
        {
            var indice = (int)(fila.RowIndex?.Value ?? (uint)(filas.Count + 1));
            while (filas.Count < indice - 1)
                filas.Add([]);
            var celdas = new Dictionary<int, CeldaLeida>();
            var columna = 0;
            foreach (var celda in fila.Elements<Cell>())
            {
                columna = celda.CellReference?.Value is { } r ? Columna(r) : columna + 1;
                var valor = celda.CellValue?.Text ?? celda.InlineString?.InnerText ?? string.Empty;
                if (celda.DataType?.Value == CellValues.SharedString && int.TryParse(valor, out var i) && i < compartidos.Count)
                    celdas[columna] = new CeldaLeida(compartidos[i], null);
                else if (celda.DataType?.Value is null || celda.DataType.Value == CellValues.Number)
                    celdas[columna] = new CeldaLeida(valor, double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null);
                else
                    celdas[columna] = new CeldaLeida(valor, null);
            }

            filas.Add(celdas);
        }

        return filas;
    }

    /// <summary>«C12» → 3.</summary>
    private static int Columna(string referencia)
    {
        var n = 0;
        foreach (var c in referencia.TakeWhile(char.IsAsciiLetter))
            n = (n * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        return n;
    }

    /// <summary>Fecha de Excel: número de serie, «dd/mm/aaaa» o «aaaa-mm-dd».</summary>
    public static DateOnly? Fecha(CeldaLeida? c)
    {
        if (c is null)
            return null;
        if (c.Numero is { } serial && serial is > 1 and < 100000)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        var texto = c.Texto.Trim();
        return DateOnly.TryParseExact(texto, ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    /// <summary>Valor en pesos: celda numérica o texto con separadores de miles «.» o «,».</summary>
    public static decimal? Valor(CeldaLeida? c)
    {
        if (c is null)
            return null;
        if (c.Numero is { } n)
            return (decimal)n;
        var limpio = new string(c.Texto.Where(ch => char.IsAsciiDigit(ch) || ch is '.' or ',').ToArray());
        if (limpio.Length == 0)
            return null;
        // «1.234.567» o «1,234,567» → separadores de miles; «1234,50» → decimal con coma.
        var ultimo = Math.Max(limpio.LastIndexOf('.'), limpio.LastIndexOf(','));
        var decimales = ultimo >= 0 && limpio.Length - ultimo - 1 is 1 or 2;
        var entero = decimales ? limpio[..ultimo] : limpio;
        var fraccion = decimales ? limpio[(ultimo + 1)..] : "0";
        entero = new string(entero.Where(char.IsAsciiDigit).ToArray());
        if (!decimal.TryParse($"{entero}.{fraccion}", NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
            return null;
        return c.Texto.TrimStart().StartsWith('-') ? -v : v;
    }
}
