using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Impuestos.Api.Infraestructura.Excel;
using Flit.Impuestos.Api.Modulos.Carga;
using Flit.Impuestos.Api.Persistence.Siembra;
using FluentAssertions;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Carga;

/// <summary>
/// HU #14694 (M-3, bomba XLSX) — <see cref="LectorXlsx"/> con libros sintéticos inflados, sin base ni API: un libro cuyo contenido
/// descomprimido declarado pasa de <see cref="LectorXlsx.MaximoBytesDescomprimidos"/>, con una parte de más de
/// <see cref="LectorXlsx.MaximoCaracteresPorParte"/> caracteres o con más de <see cref="LectorXlsx.MaximoCadenasCompartidas"/>
/// cadenas compartidas no se lee (null ⇒ <c>FORMATO_NO_SOPORTADO</c>, el literal que ya existe), y la carga deja de leer filas
/// en el tope de placas + 1.
/// </summary>
/// <example>
/// <code>
/// LectorXlsx.Leer(bytes, maximoFilas: 5001)?.Filas.Count; // a lo sumo 5001
/// </code>
/// </example>
public sealed class LectorXlsxTests
{
    private static readonly string?[] Encabezado = ["placa"];

    [Fact]
    public void Un_libro_normal_se_lee_igual_que_antes()
    {
        var tabla = LectorXlsx.Leer(M.Xlsx([["placa"], ["ABC123"], ["DEF456"]]), maximoFilas: 10);

        tabla.Should().NotBeNull();
        tabla!.Encabezados.Should().Equal("placa");
        tabla.Filas.Select(f => f.Celdas[0]).Should().Equal("ABC123", "DEF456");
    }

    [Fact]
    public void Un_libro_cuyo_contenido_descomprimido_declarado_pasa_del_maximo_no_se_abre()
    {
        var libro = ConEntradaExtra(M.Xlsx([["placa"], ["ABC123"]]), "xl/media/relleno.bin", LectorXlsx.MaximoBytesDescomprimidos + 1);

        libro.Length.Should().BeLessThan(AnalizadorCarga.MaximoBytes, "es un zip pequeño que se infla al abrirlo");
        LectorXlsx.Leer(libro, maximoFilas: 10).Should().BeNull();
    }

    [Fact]
    public void Una_parte_con_mas_caracteres_que_el_tope_no_se_lee()
    {
        var libro = M.Xlsx([["placa"], [new string('A', (int)LectorXlsx.MaximoCaracteresPorParte + 1)]]);

        LectorXlsx.MaximoCaracteresPorParte.Should().BeLessThanOrEqualTo(16L * 1024 * 1024);
        LectorXlsx.Leer(libro, maximoFilas: 10).Should().BeNull();
    }

    [Fact]
    public void Mas_cadenas_compartidas_que_el_tope_no_se_lee()
    {
        var libro = ConCadenasCompartidas(LectorXlsx.MaximoCadenasCompartidas + 1);

        LectorXlsx.Leer(libro, maximoFilas: 10).Should().BeNull();
    }

    [Fact]
    public void Deja_de_leer_filas_en_el_maximo_pedido()
    {
        var filas = new List<string?[]> { Encabezado };
        filas.AddRange(Enumerable.Range(0, 30).Select(i => new string?[] { $"ABC{i:000}" }));

        LectorXlsx.Leer(M.Xlsx(filas), maximoFilas: 11)!.Filas.Should().HaveCount(11);
    }

    [Fact]
    public void La_carga_corta_en_el_tope_de_placas_mas_uno()
    {
        var tope = ConfiguracionPorDefecto.MaxRegistrosLoteMaximo;
        var filas = new List<string?[]> { Encabezado };
        filas.AddRange(Enumerable.Range(0, tope + 500).Select(i => new string?[] { Placa(i) }));

        var carga = AnalizadorCarga.Analizar(new ArchivoCarga("placas.xlsx", M.Xlsx(filas)));

        carga.Placas.Should().HaveCount(tope + 1, "basta una placa más que el tope para saber que lo supera");
        carga.Resumen(tope).SuperaTope.Should().BeTrue();
    }

    /// <summary>Placa distinta por índice con la forma AAA000.</summary>
    private static string Placa(int i) => $"{(char)('A' + (i / 1000 % 26))}{(char)('A' + (i / 26000 % 26))}Z{i % 1000:000}";

    /// <summary>Copia del libro con una entrada más de <paramref name="bytes"/> ceros (se comprime a casi nada).</summary>
    private static byte[] ConEntradaExtra(byte[] libro, string nombre, long bytes)
    {
        using var flujo = new MemoryStream();
        flujo.Write(libro);
        using (var zip = new ZipArchive(flujo, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var entrada = zip.CreateEntry(nombre, CompressionLevel.SmallestSize).Open();
            var bloque = new byte[1024 * 1024];
            for (var restante = bytes; restante > 0; restante -= bloque.Length)
                entrada.Write(bloque, 0, (int)Math.Min(bloque.Length, restante));
        }

        return flujo.ToArray();
    }

    /// <summary>Libro con <paramref name="cantidad"/> cadenas compartidas y una hoja que usa la primera.</summary>
    private static byte[] ConCadenasCompartidas(int cantidad)
    {
        using var flujo = new MemoryStream();
        using (var libro = SpreadsheetDocument.Create(flujo, SpreadsheetDocumentType.Workbook))
        {
            var partes = libro.AddWorkbookPart();
            partes.Workbook = new Workbook();
            var cadenas = partes.AddNewPart<SharedStringTablePart>();
            using (var escritor = OpenXmlWriter.Create(cadenas))
            {
                escritor.WriteStartElement(new SharedStringTable());
                for (var i = 0; i < cantidad; i++)
                    escritor.WriteElement(new SharedStringItem(new Text(i == 0 ? "placa" : "x")));
                escritor.WriteEndElement();
            }

            var hoja = partes.AddNewPart<WorksheetPart>();
            hoja.Worksheet = new Worksheet(new SheetData(new Row(new Cell { CellReference = "A1", DataType = CellValues.SharedString, CellValue = new CellValue("0") }) { RowIndex = 1 }));
            partes.Workbook.AppendChild(new Sheets(new Sheet { Id = partes.GetIdOfPart(hoja), SheetId = 1, Name = "Hoja1" }));
        }

        return flujo.ToArray();
    }
}
