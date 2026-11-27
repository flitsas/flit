using System.IO.Compression;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Impuestos.Api.Infraestructura.Excel;
using Flit.Impuestos.Api.Modulos.Carga;
using Flit.Impuestos.Api.Persistence.Siembra;
using Flit.Impuestos.Api.Plataforma;
using FluentAssertions;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;

namespace Flit.Impuestos.Tests.Carga;

/// <summary>
/// HU #14694 (M-3, bomba XLSX) — <see cref="LectorXlsx"/> con libros sintéticos inflados, sin base ni API: un libro cuyo contenido
/// descomprimido declarado pasa de <see cref="LectorXlsx.MaximoBytesDescomprimidos"/>, con una parte de más de
/// <see cref="LectorXlsx.MaximoCaracteresPorParte"/> caracteres o con más de <see cref="LectorXlsx.MaximoCadenasCompartidas"/>
/// cadenas compartidas no se lee (null ⇒ <c>FORMATO_NO_SOPORTADO</c>, el literal que ya existe), y la carga deja de leer filas
/// en el tope de placas únicas válidas + 1, con un tope absoluto de filas leídas y de columnas (B-1, N-2), igual en CSV.
/// </summary>
/// <example>
/// <code>
/// LectorXlsx.Leer(bytes, maximoFilasLeidas: 20_000, (encabezados, fila) => sigue); // null si pasa de 20.000 filas
/// </code>
/// </example>
public sealed class LectorXlsxTests
{
    private static readonly string?[] Encabezado = ["placa"];

    [Fact]
    public void Un_libro_normal_se_lee_igual_que_antes()
    {
        var tabla = LectorXlsx.Leer(M.Xlsx([["placa"], ["ABC123"], ["DEF456"]]), maximoFilasLeidas: 10);

        tabla.Should().NotBeNull();
        tabla!.Encabezados.Should().Equal("placa");
        tabla.Filas.Select(f => f.Celdas[0]).Should().Equal("ABC123", "DEF456");
    }

    [Fact]
    public void Un_libro_cuyo_contenido_descomprimido_declarado_pasa_del_maximo_no_se_abre()
    {
        var libro = ConEntradaExtra(M.Xlsx([["placa"], ["ABC123"]]), "xl/media/relleno.bin", LectorXlsx.MaximoBytesDescomprimidos + 1);

        libro.Length.Should().BeLessThan(AnalizadorCarga.MaximoBytes, "es un zip pequeño que se infla al abrirlo");
        LectorXlsx.Leer(libro, maximoFilasLeidas: 10).Should().BeNull();
    }

    [Fact]
    public void Una_parte_con_mas_caracteres_que_el_tope_no_se_lee()
    {
        var libro = M.Xlsx([["placa"], [new string('A', (int)LectorXlsx.MaximoCaracteresPorParte + 1)]]);

        LectorXlsx.MaximoCaracteresPorParte.Should().BeLessThanOrEqualTo(16L * 1024 * 1024);
        LectorXlsx.Leer(libro, maximoFilasLeidas: 10).Should().BeNull();
    }

    [Fact]
    public void Mas_cadenas_compartidas_que_el_tope_no_se_lee()
    {
        var libro = ConCadenasCompartidas(LectorXlsx.MaximoCadenasCompartidas + 1);

        LectorXlsx.Leer(libro, maximoFilasLeidas: 10).Should().BeNull();
    }

    [Fact]
    public void Deja_de_leer_filas_cuando_seguir_lo_pide()
    {
        var filas = new List<string?[]> { Encabezado };
        filas.AddRange(Enumerable.Range(0, 30).Select(i => new string?[] { $"ABC{i:000}" }));
        var entregadas = 0;

        var tabla = LectorXlsx.Leer(M.Xlsx(filas), maximoFilasLeidas: 100, (_, _) => ++entregadas < 11);

        tabla!.Filas.Should().HaveCount(11);
        entregadas.Should().Be(11);
    }

    [Fact]
    public void Pasar_el_maximo_de_filas_leidas_no_se_lee()
    {
        var filas = new List<string?[]> { Encabezado };
        filas.AddRange(Enumerable.Range(0, 30).Select(i => new string?[] { $"ABC{i:000}" }));

        LectorXlsx.Leer(M.Xlsx(filas), maximoFilasLeidas: 31).Should().NotBeNull("encabezado + 30 filas");
        LectorXlsx.Leer(M.Xlsx(filas), maximoFilasLeidas: 30).Should().BeNull();
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

    /// <summary>Tope absoluto de filas leídas que fija el encargo B-1/N-2: 4 veces el tope de placas más alto.</summary>
    private const int TopeFilasLeidas = 4 * ConfiguracionPorDefecto.MaxRegistrosLoteMaximo;

    /// <summary>Tope de columnas que fija N-2.</summary>
    private const int TopeColumnas = 64;

    [Fact]
    public void B1_cinco_mil_placas_unicas_con_filas_repetidas_e_invalidas_dan_cinco_mil_placas_sin_superar_el_tope()
    {
        var tope = ConfiguracionPorDefecto.MaxRegistrosLoteMaximo;
        var filas = new List<string?[]> { Encabezado, F(Placa(0)), F(Placa(0)), F("AB.12"), F(Placa(1)), F(Placa(1)) };
        filas.AddRange(Enumerable.Range(2, tope - 2).Select(i => new string?[] { Placa(i) }));

        var carga = AnalizadorCarga.Analizar(new ArchivoCarga("placas.xlsx", M.Xlsx(filas)));

        carga.Placas.Should().HaveCount(tope, "el tope se mide sobre placas únicas válidas, no sobre filas leídas");
        carga.Duplicadas.Should().Be(2);
        carga.Invalidas.Should().ContainSingle().Which.Motivo.Should().Be(AnalizadorCarga.MotivoInvalida);
        carga.Resumen(tope).SuperaTope.Should().BeFalse();
    }

    [Fact]
    public void B1_csv_y_xlsx_con_el_mismo_contenido_dan_el_mismo_resultado_y_ambos_cortan_en_el_tope_mas_uno()
    {
        var tope = ConfiguracionPorDefecto.MaxRegistrosLoteMaximo;
        var filas = new List<string?[]> { Encabezado, F(Placa(0)), F(Placa(0)), F("AB.12") };
        filas.AddRange(Enumerable.Range(1, tope - 1).Select(i => new string?[] { Placa(i) }));
        filas.Add(F("ZZ.99"));
        filas.Add(F(Placa(3)));
        filas.AddRange(Enumerable.Range(tope, 10).Select(i => new string?[] { Placa(i) }));
        filas.Add(F(Placa(5)));

        var xlsx = AnalizadorCarga.Analizar(new ArchivoCarga("placas.xlsx", M.Xlsx(filas)));
        var csv = AnalizadorCarga.Analizar(new ArchivoCarga("placas.csv", M.Csv([.. filas.Select(f => f[0] ?? string.Empty)])));

        xlsx.Placas.Should().HaveCount(tope + 1, "basta una placa más que el tope para saber que lo supera");
        csv.Placas.Should().Equal(xlsx.Placas);
        csv.Duplicadas.Should().Be(xlsx.Duplicadas).And.Be(2, "las filas después del corte no se cuentan en ningún formato");
        csv.Invalidas.Should().Equal(xlsx.Invalidas);
        csv.Invalidas.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("placas.xlsx")]
    [InlineData("placas.csv")]
    public void B1_pasar_el_tope_absoluto_de_filas_leidas_sin_llegar_al_tope_de_placas_rechaza_el_archivo(string nombre)
    {
        var filas = new List<string?[]> { Encabezado };
        filas.AddRange(Enumerable.Range(0, TopeFilasLeidas).Select(i => new string?[] { i % 2 == 0 ? Placa(i % 100) : "AB.12" }));
        var contenido = nombre.EndsWith(".xlsx", StringComparison.Ordinal) ? M.Xlsx(filas) : M.Csv([.. filas.Select(f => f[0] ?? string.Empty)]);

        var accion = () => AnalizadorCarga.Analizar(new ArchivoCarga(nombre, contenido));

        var error = accion.Should().Throw<ReglaException>().Which;
        error.Codigo.Should().Be(ErroresCarga.FormatoNoSoportado);
        error.Status.Should().Be(415);
    }

    [Fact]
    public void N2_las_celdas_mas_alla_del_tope_de_columnas_se_ignoran()
    {
        var libro = ConCeldasEn([("A1", "placa"), ("A2", "ABC123"), ("ZZZ2", "x"), ("BM2", "y")]);

        var tabla = LectorXlsx.Leer(libro, 10);

        tabla.Should().NotBeNull();
        tabla!.Filas.Should().ContainSingle();
        tabla.Filas[0].Celdas.Count.Should().BeLessThanOrEqualTo(TopeColumnas, "no se rellena la lista hasta la columna ZZZ");
        tabla.Filas[0].Celdas[0].Should().Be("ABC123");
    }

    [Fact]
    public void N2_las_filas_vacias_de_un_xlsx_cuentan_para_el_tope_absoluto_de_filas_leidas()
    {
        var celdas = new List<(string, string)> { ("A1", "placa") };
        celdas.AddRange(Enumerable.Range(2, TopeFilasLeidas).Select(f => ($"ZZZ{f}", string.Empty)));
        var libro = ConCeldasEn(celdas);

        libro.Length.Should().BeLessThan(AnalizadorCarga.MaximoBytes);
        var accion = () => AnalizadorCarga.Analizar(new ArchivoCarga("placas.xlsx", libro));

        accion.Should().Throw<ReglaException>().Which.Codigo.Should().Be(ErroresCarga.FormatoNoSoportado);
    }

    [Fact]
    public void N2_las_lineas_vacias_de_un_csv_cuentan_para_el_tope_absoluto_de_filas_leidas()
    {
        var lineas = new List<string> { "placa", "ABC123" };
        lineas.AddRange(Enumerable.Repeat(string.Empty, TopeFilasLeidas));

        var accion = () => AnalizadorCarga.Analizar(new ArchivoCarga("placas.csv", M.Csv([.. lineas])));

        accion.Should().Throw<ReglaException>().Which.Codigo.Should().Be(ErroresCarga.FormatoNoSoportado);
    }

    /// <summary>Fila de una sola columna.</summary>
    private static string?[] F(string? valor) => [valor];

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

    /// <summary>Libro con una celda de texto en línea por referencia (p. ej. <c>ZZZ2</c>), agrupadas en filas por su número.</summary>
    private static byte[] ConCeldasEn(IEnumerable<(string Referencia, string Valor)> celdas)
    {
        using var flujo = new MemoryStream();
        using (var libro = SpreadsheetDocument.Create(flujo, SpreadsheetDocumentType.Workbook))
        {
            var partes = libro.AddWorkbookPart();
            partes.Workbook = new Workbook();
            var hoja = partes.AddNewPart<WorksheetPart>();
            using (var escritor = OpenXmlWriter.Create(hoja))
            {
                escritor.WriteStartElement(new Worksheet());
                escritor.WriteStartElement(new SheetData());
                foreach (var grupo in celdas.GroupBy(c => uint.Parse(c.Referencia.AsSpan(c.Referencia.AsSpan().IndexOfAnyInRange('0', '9')), System.Globalization.CultureInfo.InvariantCulture)))
                {
                    escritor.WriteStartElement(new Row { RowIndex = grupo.Key });
                    foreach (var (referencia, valor) in grupo)
                        escritor.WriteElement(new Cell { CellReference = referencia, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(valor)) });
                    escritor.WriteEndElement();
                }

                escritor.WriteEndElement();
                escritor.WriteEndElement();
            }

            partes.Workbook.AppendChild(new Sheets(new Sheet { Id = partes.GetIdOfPart(hoja), SheetId = 1, Name = "Hoja1" }));
        }

        return flujo.ToArray();
    }
}
