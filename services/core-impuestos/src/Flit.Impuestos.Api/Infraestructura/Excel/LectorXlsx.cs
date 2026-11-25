using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Flit.Impuestos.Api.Infraestructura.Excel;

/// <summary>Una fila leída de una hoja o de un CSV: su número (el de la hoja o la línea) y el texto de cada columna (null = vacía).</summary>
public sealed record FilaLeida(int Numero, IReadOnlyList<string?> Celdas);

/// <summary>Encabezados (la primera fila con algún valor) y las filas que siguen con algún valor.</summary>
public sealed record TablaLeida(IReadOnlyList<string?> Encabezados, IReadOnlyList<FilaLeida> Filas);

/// <summary>
/// Lector de .xlsx de la carga masiva (HU #14694, M12) sobre <c>DocumentFormat.OpenXml</c> en modo SAX, con la misma forma que el
/// lector de la carga de Diagnóstico (sin su código, EP-CF1): primera hoja del libro, columnas resueltas del atributo <c>r</c> de
/// cada celda (Excel omite las vacías) y texto de cadena compartida, en línea, fórmula o número (el valor tal como lo guarda el libro).
/// El número de fila es el de la hoja. Contra un zip inflado (M-3): antes de abrir el libro se suma el tamaño descomprimido que declara
/// cada entrada (tope <see cref="MaximoBytesDescomprimidos"/>), cada parte se lee con tope de caracteres
/// (<see cref="MaximoCaracteresPorParte"/>), las cadenas compartidas tienen tope (<see cref="MaximoCadenasCompartidas"/>), las celdas
/// después de la columna <see cref="MaximoColumnas"/> se ignoran (N-2) y toda fila del XML cuenta contra <c>maximoFilasLeidas</c>,
/// también el encabezado y las vacías (B-1, N-2). Quien llama corta antes la lectura con <c>seguir</c> (la carga, al juntar el tope de
/// placas únicas válidas más una). Un libro que no se puede abrir o que pasa un tope devuelve null: quien llama decide el error (sin
/// la causa, que puede citar el contenido).
/// </summary>
/// <example>
/// <code>
/// var tabla = LectorXlsx.Leer(bytes, maximoFilasLeidas: 20_000, (encabezados, fila) => sigue) ?? throw ErroresCarga.Formato();
/// </code>
/// </example>
public static class LectorXlsx
{
    /// <summary>Tope de caracteres por parte del libro (16 M: de sobra para el tope de placas de un lote).</summary>
    public const long MaximoCaracteresPorParte = 16L * 1024 * 1024;

    /// <summary>Tope de la suma del tamaño descomprimido que declaran las entradas del zip (50 MB, 10 veces el tope del archivo).</summary>
    public const long MaximoBytesDescomprimidos = 50L * 1024 * 1024;

    /// <summary>Tope de cadenas compartidas del libro.</summary>
    public const int MaximoCadenasCompartidas = 250_000;

    /// <summary>
    /// Columnas que se leen de cada fila (N-2). La plantilla oficial (<see cref="Modulos.Carga.PlantillaCarga"/>) trae una sola
    /// (<c>placa</c>, columna A); 64 deja sitio de sobra a una hoja propia de la compañía con columnas extra sin rellenar cada fila
    /// hasta la columna <c>ZZZ</c> (18.278 posiciones).
    /// </summary>
    public const int MaximoColumnas = 64;

    /// <summary>
    /// Lee la primera hoja. Cada fila con valor después del encabezado va a <paramref name="seguir"/> (si viene) y la lectura para
    /// cuando devuelve false. Más de <paramref name="maximoFilasLeidas"/> filas leídas (encabezado, vacías y con valor) ⇒ null.
    /// </summary>
    public static TablaLeida? Leer(byte[] contenido, int maximoFilasLeidas, Func<IReadOnlyList<string?>, FilaLeida, bool>? seguir = null)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximoFilasLeidas, 1);
        try
        {
            using var flujo = new MemoryStream(contenido, writable: false);
            if (DescomprimidoDeclarado(flujo) > MaximoBytesDescomprimidos)
                return null;
            flujo.Position = 0;
            using var libro = SpreadsheetDocument.Open(flujo, false, new OpenSettings { MaxCharactersInPart = MaximoCaracteresPorParte });
            return Leer(libro, maximoFilasLeidas, seguir);
        }
#pragma warning disable CA1031 // Cualquier fallo al abrir o recorrer el libro es «el archivo del usuario no se puede leer».
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    /// <summary>Suma del tamaño descomprimido que declara el directorio central del zip (sin descomprimir nada).</summary>
    private static long DescomprimidoDeclarado(Stream flujo)
    {
        using var zip = new ZipArchive(flujo, ZipArchiveMode.Read, leaveOpen: true);
        var total = 0L;
        foreach (var entrada in zip.Entries)
        {
            total += entrada.Length;
            if (total > MaximoBytesDescomprimidos)
                break;
        }

        return total;
    }

    private static TablaLeida? Leer(SpreadsheetDocument libro, int maximoFilasLeidas, Func<IReadOnlyList<string?>, FilaLeida, bool>? seguir)
    {
        var partes = libro.WorkbookPart;
        var hoja = partes?.Workbook?.Sheets?.Elements<Sheet>().FirstOrDefault();
        if (partes is null || hoja?.Id?.Value is null || partes.GetPartById(hoja.Id.Value) is not WorksheetPart parteHoja)
            return null;

        var compartidas = CadenasCompartidas(partes);
        List<string?>? encabezados = null;
        var filas = new List<FilaLeida>();
        var leidas = 0;
        using var lector = OpenXmlReader.Create(parteHoja);
        while (lector.Read())
        {
            if (lector.ElementType != typeof(Row) || !lector.IsStartElement)
                continue;
            if (++leidas > maximoFilasLeidas)
                return null;

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
            {
                var leida = new FilaLeida(numero, celdas);
                filas.Add(leida);
                if (seguir is not null && !seguir(encabezados, leida))
                    break;
            }
        }

        return new TablaLeida(encabezados ?? [], filas);
    }

    private static List<string> CadenasCompartidas(WorkbookPart partes)
    {
        var valores = new List<string>();
        if (partes.SharedStringTablePart is not { } parte)
            return valores;

        using var lector = OpenXmlReader.Create(parte);
        while (lector.Read())
        {
            if (lector.ElementType != typeof(SharedStringItem) || !lector.IsStartElement)
                continue;
            if (valores.Count >= MaximoCadenasCompartidas)
                throw new InvalidDataException("El libro pasa el tope de cadenas compartidas.");
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
            if (columna >= MaximoColumnas)
                continue;
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
}
