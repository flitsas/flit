using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Carga;

/// <summary>
/// Lector de .xlsx de la carga masiva (HU #13749, F02-H4) sobre <c>DocumentFormat.OpenXml</c> en modo SAX, el mismo enfoque
/// que <c>BulkTramitesXlsxParser</c> de core-api: primera hoja del libro, columnas resueltas del atributo <c>r</c> de cada
/// celda (Excel omite las vacías) y texto de cadena compartida, en línea, fórmula o número (el valor tal como lo guarda el
/// libro, sin formato regional). El número de fila es el de la hoja. Un libro que no se puede abrir es 415
/// <c>ARCHIVO_ILEGIBLE</c>; cada parte se lee con un tope de caracteres para que un zip inflado no agote la memoria.
/// </summary>
/// <example>
/// <code>
/// LectorXlsx.Leer(File.ReadAllBytes("carga.xlsx")) // → TablaCarga(encabezados, filas)
/// </code>
/// </example>
internal static class LectorXlsx
{
    /// <summary>Tope de caracteres por parte del libro (unas 50 veces el tope de tamaño por defecto del archivo).</summary>
    private const long MaximoCaracteresPorParte = 256L * 1024 * 1024;

    public static TablaCarga Leer(byte[] contenido)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        try
        {
            using var flujo = new MemoryStream(contenido, writable: false);
            using var libro = SpreadsheetDocument.Open(flujo, false, new OpenSettings { MaxCharactersInPart = MaximoCaracteresPorParte });
            return Leer(libro) ?? throw Ilegible();
        }
        catch (ReglaException)
        {
            throw;
        }
#pragma warning disable CA1031 // Cualquier fallo al abrir o recorrer el libro es «el archivo del usuario no se puede leer».
        catch (Exception)
#pragma warning restore CA1031
        {
            // Sin la causa: el detalle del parser puede citar el contenido del archivo.
            throw Ilegible();
        }
    }

    private static TablaCarga? Leer(SpreadsheetDocument libro)
    {
        var partes = libro.WorkbookPart;
        var hoja = partes?.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault();
        if (partes is null || hoja?.Id?.Value is null || partes.GetPartById(hoja.Id.Value) is not WorksheetPart parteHoja)
            return null;

        var compartidas = CadenasCompartidas(partes);
        List<string?>? encabezados = null;
        var filas = new List<FilaLeida>();
        using var lector = OpenXmlReader.Create(parteHoja);
        while (lector.Read())
        {
            if (lector.ElementType != typeof(Row) || !lector.IsStartElement)
                continue;

            var fila = (Row)lector.LoadCurrentElement()!;
            var celdas = Celdas(fila, compartidas);
            var numero = (int)(fila.RowIndex?.Value ?? (uint)(filas.Count + (encabezados is null ? 1 : 2)));
            if (encabezados is null)
            {
                if (celdas.All(string.IsNullOrWhiteSpace))
                    continue;
                encabezados = celdas;
                continue;
            }

            if (celdas.Any(c => !string.IsNullOrWhiteSpace(c)))
                filas.Add(new FilaLeida(numero, celdas));
        }

        return new TablaCarga(encabezados ?? [], filas);
    }

    private static List<string> CadenasCompartidas(WorkbookPart partes)
    {
        var valores = new List<string>();
        if (partes.SharedStringTablePart is not { } parte)
            return valores;

        using var lector = OpenXmlReader.Create(parte);
        while (lector.Read())
        {
            if (lector.ElementType == typeof(SharedStringItem) && lector.IsStartElement)
                valores.Add(lector.LoadCurrentElement()?.InnerText ?? string.Empty);
        }

        return valores;
    }

    private static List<string?> Celdas(Row fila, List<string> compartidas)
    {
        var celdas = new List<string?>();
        var siguiente = 0;
        foreach (var celda in fila.Elements<Cell>())
        {
            var columna = Columna(celda.CellReference?.Value) ?? siguiente;
            siguiente = columna + 1;
            while (celdas.Count <= columna)
                celdas.Add(null);
            celdas[columna] = Valor(celda, compartidas);
        }

        return celdas;
    }

    /// <summary>Índice de columna (base 0) de una referencia como <c>C12</c>; null si no trae letras.</summary>
    private static int? Columna(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia))
            return null;

        var indice = 0;
        var letras = 0;
        foreach (var c in referencia)
        {
            if (!char.IsAsciiLetter(c))
                break;
            indice = (indice * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
            letras++;
            if (letras > 3)
                return null;
        }

        return letras == 0 ? null : indice - 1;
    }

    private static string? Valor(Cell celda, List<string> compartidas)
    {
        var tipo = celda.DataType?.Value;
        if (tipo == CellValues.SharedString)
        {
            return int.TryParse(celda.CellValue?.Text, out var indice) && indice >= 0 && indice < compartidas.Count
                ? compartidas[indice]
                : null;
        }

        return tipo == CellValues.InlineString ? celda.InlineString?.InnerText : celda.CellValue?.Text;
    }

    private static ReglaException Ilegible() => new(
        FormatoCarga.CodigoIlegible,
        "El archivo .xlsx está dañado o no es un libro de Excel: ábralo en Excel, guárdelo de nuevo como .xlsx y vuelva a subirlo.",
        StatusCodes.Status415UnsupportedMediaType);
}
