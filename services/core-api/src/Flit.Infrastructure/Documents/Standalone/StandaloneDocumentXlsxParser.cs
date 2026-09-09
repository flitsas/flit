using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Admin.Application.GeneracionDocumental.Batches;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Infrastructure.Documents.Standalone;

/// <summary>
/// Lector de la plantilla XLSX v1 (CF-11, Feature #12201 I3) sobre <c>DocumentFormat.OpenXml</c>
/// <b>crudo</b>, en modo SAX (<see cref="OpenXmlReader"/>).
///
/// <para><b>Sin ClosedXML ni EPPlus</b> a propósito (§8.4 del diseño): una dependencia nueva exige
/// auditoría previa (regla FLIT 18) y el diseño no la asume. <c>DocumentFormat.OpenXml</c> ya está
/// en <c>Flit.Infrastructure.csproj</c> y es lo que se usa.</para>
///
/// <para><b>Las tres trampas del OpenXml crudo, y cómo se resuelven aquí:</b></para>
/// <list type="number">
/// <item><b>Celdas vacías OMITIDAS.</b> Excel no escribe la celda de una casilla vacía: la fila
/// <c>A,C,D</c> en el XML es <c>A,C,D</c>, no <c>A,_,C,D</c>. Contar posiciones desalinea la fila
/// entera y mete el domicilio en la columna del precio. Aquí la columna real se resuelve
/// SIEMPRE del atributo <c>r</c> de cada celda (<c>"C7"</c> → columna C → índice 2), nunca del
/// orden de aparición. Es lo mismo que hace <see cref="ColumnIndex"/>.</item>
/// <item><b><c>t="s"</c> frente a <c>t="inlineStr"</c>.</b> Un texto puede venir como índice a la
/// tabla de cadenas compartidas o embebido en la celda —y hasta como <c>str</c> si es el
/// resultado de una fórmula—. Se resuelven los tres casos; ignorar el primero devolvería
/// números de índice como si fueran datos.</item>
/// <item><b>El serial numérico de fechas.</b> Una celda de fecha «real» de Excel es un número
/// (45 000 = 2023-03-15) y convertirlo a ciegas produce documentos con la fecha equivocada.
/// Como la plantilla declara TODAS las columnas como texto, una celda numérica en una columna de
/// fecha es un archivo que no siguió la plantilla: se rechaza <b>esa fila</b> con
/// <c>invalid_date</c> y el lote continúa (CF-13). No se intenta convertir el serial.</item>
/// </list>
///
/// <para>Un archivo del usuario que no es un XLSX no es una condición excepcional del servidor: se
/// devuelve <c>invalid_file</c> y el handler responde 422 <b>sin escribir nada en storage</b>.</para>
/// </summary>
internal sealed class StandaloneDocumentXlsxParser : IStandaloneDocumentXlsxParser
{
    /// <summary>Firma local de un archivo ZIP. Todo XLSX es un paquete OPC, que es un ZIP.</summary>
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    public StandaloneBatchParseResult Parse(Stream xlsx)
    {
        ArgumentNullException.ThrowIfNull(xlsx);

        // El paquete OPC necesita un stream buscable; el cuerpo multipart no siempre lo es.
        using var buffer = new MemoryStream();
        if (xlsx.CanSeek)
        {
            xlsx.Position = 0;
        }

        xlsx.CopyTo(buffer);
        buffer.Position = 0;

        // MIME REAL: se verifica el contenido, no la extensión ni el Content-Type declarado.
        if (!TieneFirmaZip(buffer))
        {
            return StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.InvalidFile);
        }

        buffer.Position = 0;

        try
        {
            using var document = SpreadsheetDocument.Open(buffer, isEditable: false);
            return Read(document);
        }
#pragma warning disable CA1031 // Cualquier fallo de apertura es «el archivo del usuario no sirve».
        catch (Exception)
#pragma warning restore CA1031
        {
            return StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.InvalidFile);
        }
    }

    private static bool TieneFirmaZip(MemoryStream buffer)
    {
        Span<byte> head = stackalloc byte[4];
        return buffer.Length >= 4
            && buffer.Read(head) == 4
            && head.SequenceEqual(ZipSignature);
    }

    private static StandaloneBatchParseResult Read(SpreadsheetDocument document)
    {
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.InvalidFile);
        }

        var sharedStrings = LeerCadenasCompartidas(workbookPart);

        var sheet = workbookPart.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault();
        if (sheet?.Id?.Value is null
            || workbookPart.GetPartById(sheet.Id.Value) is not WorksheetPart worksheetPart)
        {
            return StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.InvalidFile);
        }

        var rows = new List<StandaloneBatchParsedRow>();
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

            // Fila 1 = encabezado. Si el XLSX arranca en otra fila, tampoco es la plantilla v1.
            if (!headerLeido)
            {
                if (!StandaloneBatchTemplate.HeaderMatches(AEncabezado(celdas)))
                {
                    return StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.TemplateInvalid);
                }

                headerLeido = true;
                continue;
            }

            if (celdas.Count == 0 || celdas.Values.All(c => string.IsNullOrWhiteSpace(c.Text)))
            {
                // Fila con solo formato: Excel las deja al borrar contenido. No es una fila de datos.
                continue;
            }

            // Número que ve el usuario: 1-based SIN encabezado. Sale del índice real de la fila en
            // la hoja, no de un contador propio, para que señale la fila que él ve en Excel.
            var numero = (int)(row.RowIndex?.Value ?? 0) - 1;
            if (numero < 1)
            {
                numero = rows.Count + 1;
            }

            rows.Add(AFila(numero, celdas));

            if (rows.Count > StandaloneBatchTemplate.MaxRows)
            {
                // Tope duro: se corta de inmediato, sin terminar de leer un archivo enorme.
                return StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.TooManyRows);
            }
        }

        return headerLeido
            ? new StandaloneBatchParseResult(null, rows)
            : StandaloneBatchParseResult.Rejected(StandaloneBatchParseError.TemplateInvalid);
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

    /// <summary>Celdas de la fila indexadas por su COLUMNA REAL (atributo <c>r</c>), no por posición.</summary>
    private static Dictionary<int, (string? Text, bool IsNumeric)> LeerCeldas(
        Row row,
        List<string> sharedStrings)
    {
        var celdas = new Dictionary<int, (string? Text, bool IsNumeric)>();
        var fallback = 0;

        foreach (var cell in row.Elements<Cell>())
        {
            var columna = ColumnIndex(cell.CellReference?.Value);
            if (columna < 0)
            {
                // Sin referencia (raro, pero legal en el formato): último recurso, la posición.
                columna = fallback;
            }

            fallback = columna + 1;
            celdas[columna] = ValorDeCelda(cell, sharedStrings);
        }

        return celdas;
    }

    /// <summary>
    /// Índice 0-based de la columna a partir de la referencia de celda (<c>"AB12"</c> → 27). Es lo
    /// que hace que una celda vacía omitida no desplace a las siguientes.
    /// </summary>
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

    /// <summary>
    /// Texto de la celda resolviendo shared string, inline string y resultado de fórmula.
    /// <c>IsNumeric</c> marca la celda que Excel guardó como número —sin <c>t</c> o con
    /// <c>t="n"</c>—, que es la que delata un serial de fecha.
    /// </summary>
    private static (string? Text, bool IsNumeric) ValorDeCelda(Cell cell, List<string> sharedStrings)
    {
        var tipo = cell.DataType?.Value;

        if (tipo == CellValues.SharedString)
        {
            var raw = cell.CellValue?.Text;
            return int.TryParse(raw, out var indice) && indice >= 0 && indice < sharedStrings.Count
                ? (sharedStrings[indice], false)
                : (null, false);
        }

        if (tipo == CellValues.InlineString)
        {
            return (cell.InlineString?.InnerText, false);
        }

        if (tipo == CellValues.String)
        {
            // Resultado textual de una fórmula.
            return (cell.CellValue?.Text, false);
        }

        if (tipo == CellValues.Boolean)
        {
            return (cell.CellValue?.Text == "1" ? "SI" : "NO", false);
        }

        var texto = cell.CellValue?.Text;
        return (texto, !string.IsNullOrWhiteSpace(texto));
    }

    private static List<string?> AEncabezado(Dictionary<int, (string? Text, bool IsNumeric)> celdas)
    {
        if (celdas.Count == 0)
        {
            return [];
        }

        var maximo = celdas.Keys.Max();
        var header = new List<string?>(maximo + 1);
        for (var i = 0; i <= maximo; i++)
        {
            header.Add(celdas.TryGetValue(i, out var celda) ? celda.Text : null);
        }

        return header;
    }

    private static StandaloneBatchParsedRow AFila(
        int numero,
        Dictionary<int, (string? Text, bool IsNumeric)> celdas)
    {
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var errores = new List<StandaloneDocumentValidationError>();

        for (var i = 0; i < StandaloneBatchTemplate.Columns.Count; i++)
        {
            var columna = StandaloneBatchTemplate.Columns[i];
            celdas.TryGetValue(i, out var celda);

            if (columna.IsDate && celda.IsNumeric)
            {
                // El serial NO se convierte (§8.4): la plantilla declara la columna como texto, así
                // que un número aquí significa que la celda se capturó como fecha de Excel y su
                // valor real es ambiguo (sistema 1900 vs 1904, zona horaria, formato).
                errores.Add(new StandaloneDocumentValidationError(
                    StandaloneDocumentBatchRunner.ErrorInvalidDate,
                    columna.Header,
                    "La celda de fecha se capturó como número. Escríbala como texto en formato AAAA-MM-DD."));

                valores[columna.Header] = null;
                continue;
            }

            valores[columna.Header] = celda.Text;
        }

        return new StandaloneBatchParsedRow(numero, valores, errores);
    }
}
