using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Impuestos.Api.Modulos.Comun;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary>Una celda del archivo: un número (montos y conteos) o un texto.</summary>
internal readonly record struct CeldaReporte(string Texto, decimal? Numero = null);

/// <summary>
/// Lo que un archivo de reporte (Excel o PDF) necesita: el reporte, si es la vista global, sus columnas y las filas ya convertidas a
/// celdas con los textos del contrato: los enums con su etiqueta de <c>x-flit-etiquetas</c>, el monto null con
/// <see cref="CatalogoReportes.MontoSinDato"/>, <c>parcial</c> con <see cref="CatalogoReportes.MarcaParcial"/> (vacío si no), las
/// fechas en hora de Colombia (<c>AAAA-MM-DD HH:mm</c>), las vigencias separadas por coma y la compañía por su nombre.
/// </summary>
internal sealed record ArchivoReporte(DefinicionReporte Definicion, RangoDias Rango, IReadOnlyList<ColumnaReporteDto> Columnas, IReadOnlyList<CeldaReporte[]> Filas)
{
    private static readonly HashSet<string> Fechas = new(StringComparer.Ordinal) { "creadaEn", "terminadaEn", "consultadaEn" };

    public static ArchivoReporte Armar(DefinicionReporte definicion, RangoDias rango, IReadOnlyList<ColumnaReporteDto> columnas, IReadOnlyList<object> filas)
    {
        ArgumentNullException.ThrowIfNull(definicion);
        ArgumentNullException.ThrowIfNull(columnas);
        ArgumentNullException.ThrowIfNull(filas);
        var celdas = filas.Select(f =>
        {
            var json = JsonSerializer.SerializeToElement(f, f.GetType(), JsonSerializerOptions.Web);
            return columnas.Select(c => Celda(definicion.Tipo, c.Clave, json.TryGetProperty(c.Clave, out var v) ? v : default)).ToArray();
        }).ToList();
        return new ArchivoReporte(definicion, rango, columnas, celdas);
    }

    /// <summary>Nombre del archivo: <c>reporte-impuestos-{tipo}-{desde}-{hasta}.{extension}</c>.</summary>
    public string NombreArchivo(string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"reporte-impuestos-{Definicion.Codigo}-{Rango.Desde:yyyy-MM-dd}-{Rango.Hasta:yyyy-MM-dd}.{extension}");

    private static CeldaReporte Celda(TipoReporte tipo, string clave, JsonElement valor)
    {
        if (clave == "parcial")
            return new(valor.ValueKind == JsonValueKind.True ? CatalogoReportes.MarcaParcial : string.Empty);
        switch (valor.ValueKind)
        {
            case JsonValueKind.Undefined or JsonValueKind.Null:
                return new(clave == "totalAPagar" ? CatalogoReportes.MontoSinDato : string.Empty);
            case JsonValueKind.Number:
                var numero = valor.GetDecimal();
                return new(numero.ToString(CultureInfo.InvariantCulture), numero);
            case JsonValueKind.Array:
                return new(string.Join(", ", valor.EnumerateArray().Select(v => v.ToString())));
            case JsonValueKind.Object:
                var nombre = valor.TryGetProperty("nombre", out var n) ? n.GetString() : null;
                return new(string.IsNullOrWhiteSpace(nombre) ? valor.GetProperty("id").ToString() : nombre);
            case JsonValueKind.String:
                var texto = valor.GetString() ?? string.Empty;
                if (Fechas.Contains(clave) && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instante))
                    return new(instante.ToOffset(RangoDias.OffsetColombia).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                return new(CatalogoReportes.Etiqueta(tipo, clave, texto) ?? texto);
            default:
                return new(valor.ToString());
        }
    }
}

/// <summary>
/// Excel del reporte (plan §5.1, M14) con DocumentFormat.OpenXml (la misma librería que la carga masiva de M12): una hoja «Reporte» con
/// las etiquetas de las columnas en la fila 1 y una fila por registro, sin truncar (el tope ya se comprobó). Los textos van como
/// texto en línea (una celda que empieza por <c>= + - @</c> no se evalúa como fórmula) y los montos y conteos como número.
/// </summary>
/// <example>
/// <code>
/// var bytes = ExcelReporte.Generar(ArchivoReporte.Armar(definicion, rango, columnas, filas));
/// </code>
/// </example>
internal static class ExcelReporte
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Hoja = "Reporte";

    public static byte[] Generar(ArchivoReporte archivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archivo);
        using var flujo = new MemoryStream();
        using (var libro = SpreadsheetDocument.Create(flujo, SpreadsheetDocumentType.Workbook))
        {
            var partes = libro.AddWorkbookPart();
            partes.Workbook = new Workbook();
            var parte = partes.AddNewPart<WorksheetPart>();
            var datos = new SheetData();
            datos.Append(Fila(1, archivo.Columnas.Select(c => new CeldaReporte(c.Etiqueta))));
            for (var i = 0; i < archivo.Filas.Count; i++)
            {
                if (i % 500 == 0)
                    ct.ThrowIfCancellationRequested();
                datos.Append(Fila((uint)i + 2, archivo.Filas[i]));
            }

            parte.Worksheet = new Worksheet(datos);
            partes.Workbook.AppendChild(new Sheets(new Sheet { Id = partes.GetIdOfPart(parte), SheetId = 1, Name = Hoja }));
        }

        return flujo.ToArray();
    }

    private static Row Fila(uint numero, IEnumerable<CeldaReporte> celdas)
    {
        var fila = new Row { RowIndex = numero };
        var columna = 0;
        foreach (var c in celdas)
        {
            var referencia = Columna(columna++) + numero.ToString(CultureInfo.InvariantCulture);
            fila.Append(c.Numero is { } n
                ? new Cell { CellReference = referencia, DataType = CellValues.Number, CellValue = new CellValue(n.ToString(CultureInfo.InvariantCulture)) }
                : new Cell { CellReference = referencia, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(c.Texto) { Space = SpaceProcessingModeValues.Preserve }) });
        }

        return fila;
    }

    private static string Columna(int indice)
    {
        var nombre = string.Empty;
        for (var n = indice + 1; n > 0; n = (n - 1) / 26)
            nombre = (char)('A' + ((n - 1) % 26)) + nombre;
        return nombre;
    }
}
