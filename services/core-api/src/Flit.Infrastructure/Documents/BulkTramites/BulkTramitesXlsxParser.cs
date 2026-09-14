using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Application.BulkTramites.Parsing;

namespace Flit.Infrastructure.Documents.BulkTramites;

/// <summary>
/// Lector del archivo de carga masiva de trámites (HU #12522) sobre <c>DocumentFormat.OpenXml</c>
/// crudo en modo SAX, mismo enfoque que <c>StandaloneDocumentXlsxParser</c> de Generación
/// Documental (sin ClosedXML/EPPlus — ya está en <c>Flit.Infrastructure.csproj</c>).
///
/// <para>Las columnas reales se resuelven SIEMPRE del atributo <c>r</c> de cada celda, nunca del
/// orden de aparición: Excel omite las celdas vacías del XML. Se resuelven los tres orígenes de
/// texto (cadena compartida, inline, resultado de fórmula) para no leer un índice como si fuera
/// el dato.</para>
/// </summary>
internal sealed class BulkTramitesXlsxParser : IBulkTramitesXlsxParser
{
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public BulkTramitesParseResult Parse(BulkTramitesTemplateType tipo, Stream xlsx)
    {
        ArgumentNullException.ThrowIfNull(xlsx);

        using var buffer = new MemoryStream();
        if (xlsx.CanSeek)
        {
            xlsx.Position = 0;
        }

        xlsx.CopyTo(buffer);
        buffer.Position = 0;

        if (!TieneFirmaZip(buffer))
        {
            return BulkTramitesParseResult.Rejected(BulkTramitesFileError.InvalidFile);
        }

        buffer.Position = 0;

        try
        {
            using var document = SpreadsheetDocument.Open(buffer, isEditable: false);
            return Read(tipo, document);
        }
#pragma warning disable CA1031 // Cualquier fallo de apertura es «el archivo del usuario no sirve».
        catch (Exception)
#pragma warning restore CA1031
        {
            return BulkTramitesParseResult.Rejected(BulkTramitesFileError.InvalidFile);
        }
    }

    private static bool TieneFirmaZip(MemoryStream buffer)
    {
        Span<byte> head = stackalloc byte[4];
        return buffer.Length >= 4
            && buffer.Read(head) == 4
            && head.SequenceEqual(ZipSignature);
    }

    private static BulkTramitesParseResult Read(BulkTramitesTemplateType tipo, SpreadsheetDocument document)
    {
        // Sin catálogos: los encabezados no dependen de ellos (solo los desplegables), así que el
        // parser no necesita tocar la base para saber qué columnas exigir.
        var columnas = BulkTramitesTemplateCatalog.ColumnsFor(tipo);
        var encabezadoEsperado = columnas.Select(c => c.Header).ToList();

        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return BulkTramitesParseResult.Rejected(BulkTramitesFileError.InvalidFile);
        }

        var sharedStrings = LeerCadenasCompartidas(workbookPart);

        var sheet = workbookPart.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault();
        if (sheet?.Id?.Value is null
            || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart)
        {
            return BulkTramitesParseResult.Rejected(BulkTramitesFileError.InvalidFile);
        }

        var filas = new List<BulkTramitesParsedRow>();
        var headerLeido = false;

        using var reader = OpenXmlReader.Create(worksheetPart);

        while (reader.Read())
        {
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
            {
                continue;
            }

            var row = (Row)reader.LoadCurrentElement()!;
            var celdas = LeerCeldas(row, sharedStrings);

            if (!headerLeido)
            {
                if (!HeaderMatches(AEncabezado(celdas), encabezadoEsperado))
                {
                    return BulkTramitesParseResult.Rejected(BulkTramitesFileError.TemplateInvalid);
                }

                headerLeido = true;
                continue;
            }

            if (celdas.Count == 0 || celdas.Values.All(c => string.IsNullOrWhiteSpace(c)))
            {
                continue;
            }

            var numero = (int)(row.RowIndex?.Value ?? 0) - 1;
            if (numero < 1)
            {
                numero = filas.Count + 1;
            }

            filas.Add(AFila(tipo, numero, columnas, celdas));

            if (filas.Count > BulkTramitesTemplateCatalog.MaxRows)
            {
                return BulkTramitesParseResult.Rejected(BulkTramitesFileError.TooManyRows);
            }
        }

        return headerLeido
            ? new BulkTramitesParseResult(null, filas)
            : BulkTramitesParseResult.Rejected(BulkTramitesFileError.TemplateInvalid);
    }

    private static bool HeaderMatches(List<string?> actual, List<string> esperado)
    {
        if (actual.Count < esperado.Count)
        {
            return false;
        }

        for (var i = 0; i < esperado.Count; i++)
        {
            if (!string.Equals(actual[i]?.Trim(), esperado[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> LeerCadenasCompartidas(WorkbookPart workbookPart)
    {
        var valores = new List<string>();
        var part = workbookPart.SharedStringTablePart;
        if (part is null)
        {
            return valores;
        }

        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(SharedStringItem) && reader.IsStartElement)
            {
                valores.Add(reader.LoadCurrentElement()?.InnerText ?? string.Empty);
            }
        }

        return valores;
    }

    private static Dictionary<int, string?> LeerCeldas(Row row, List<string> sharedStrings)
    {
        var celdas = new Dictionary<int, string?>();
        var fallback = 0;

        foreach (var cell in row.Elements<Cell>())
        {
            var columna = ColumnIndex(cell.CellReference?.Value);
            if (columna < 0)
            {
                columna = fallback;
            }

            fallback = columna + 1;
            celdas[columna] = ValorDeCelda(cell, sharedStrings);
        }

        return celdas;
    }

    internal static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return -1;
        }

        var indice = 0;
        var letras = 0;

        foreach (var c in cellReference)
        {
            if (c is >= 'A' and <= 'Z')
            {
                indice = (indice * 26) + (c - 'A' + 1);
                letras++;
            }
            else if (c is >= 'a' and <= 'z')
            {
                indice = (indice * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
                letras++;
            }
            else
            {
                break;
            }
        }

        return letras == 0 ? -1 : indice - 1;
    }

    private static string? ValorDeCelda(Cell cell, List<string> sharedStrings)
    {
        var tipo = cell.DataType?.Value;

        if (tipo == CellValues.SharedString)
        {
            var raw = cell.CellValue?.Text;
            return int.TryParse(raw, out var indice) && indice >= 0 && indice < sharedStrings.Count
                ? sharedStrings[indice]
                : null;
        }

        if (tipo == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText;
        }

        if (tipo == CellValues.String)
        {
            return cell.CellValue?.Text;
        }

        if (tipo == CellValues.Boolean)
        {
            return cell.CellValue?.Text == "1" ? "SI" : "NO";
        }

        return cell.CellValue?.Text;
    }

    private static List<string?> AEncabezado(Dictionary<int, string?> celdas)
    {
        if (celdas.Count == 0)
        {
            return [];
        }

        var maximo = celdas.Keys.Max();
        var header = new List<string?>(maximo + 1);
        for (var i = 0; i <= maximo; i++)
        {
            header.Add(celdas.TryGetValue(i, out var valor) ? valor : null);
        }

        return header;
    }

    private static BulkTramitesParsedRow AFila(
        BulkTramitesTemplateType tipo,
        int numero,
        IReadOnlyList<Flit.Tramites.Application.BulkTramites.BulkTramitesColumnSpec> columnas,
        Dictionary<int, string?> celdas)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < columnas.Count; i++)
        {
            celdas.TryGetValue(i, out var valor);
            valores[columnas[i].Header] = valor;
        }

        var error = BulkTramitesPercentageValidator.Validate(tipo, valores);

        return new BulkTramitesParsedRow(numero, valores, error);
    }
}
