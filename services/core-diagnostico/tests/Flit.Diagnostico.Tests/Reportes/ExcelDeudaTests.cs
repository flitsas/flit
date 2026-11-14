using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Diagnosticos;
using Flit.Diagnostico.Tests.Plataforma;
using Flit.Diagnostico.Tests.Saneamiento;
using FluentAssertions;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;
using VehiculoSembrado = Flit.Diagnostico.Tests.Tablero.VehiculoSembrado;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// HU #14576 AC38 (plan §6.5, SP-9, SP-11, SP-18, SP-21) — Excel de deuda de un vehículo (<c>GET …/deuda/excel</c>) y de varias
/// placas (<c>POST /vehiculos/deuda/excel</c>): hojas Resumen, SIMIT, Impuesto, SOAT y RTM, Saneamiento; SIMIT como texto e
/// impuesto como número; texto que parece fórmula queda literal y el libro no tiene ningún <c>&lt;f&gt;</c> ni vínculos;
/// documentos enmascarados. Se lee con <see cref="ExcelDePrueba"/> y el XML crudo del paquete.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~Reportes.ExcelDeudaTests
/// </code>
/// </example>
public sealed partial class ExcelDeudaTests
{
    private static readonly string[] Hojas = ["Resumen", "SIMIT", "Impuesto", "SOAT y RTM", "Saneamiento"];

    private static PanelDeudaDto ConFormulas() => PdfDeudaTests.Panel() with
    {
        Fuentes =
        [
            new FuenteDeudaDto("SIMIT_VEHICULO", EstadosDeuda.ConDeuda, DeudaDePrueba.Dato,
                Comparendos:
                [
                    new ComparendoDeudaDto("@SUM(1)", "2026-09-30", "450000.00", "=HYPERLINK(\"http://x\",\"y\")"),
                    new ComparendoDeudaDto("TST-C-0002", "2026-09-30", "+1200", "PENDIENTE"),
                ]),
        ],
    };

    // AC38 — una placa: las cinco hojas en orden, sin columna Placa; SIMIT como texto, impuesto como número; documento enmascarado.
    [Fact]
    public void Libro_de_una_placa_trae_las_cinco_hojas_con_tipos_correctos()
    {
        var saneamiento = new SaneamientoDeudaDto("EN_GESTION")
        {
            Renglones = [new RenglonDeudaDto("SIMIT_PROPIETARIO", "DEUDA", "PENDIENTE", null, "Nota de prueba", true)],
        };
        var bytes = ExcelDeuda.Generar([PdfDeudaTests.Panel(saneamiento: saneamiento)], conPlaca: false);
        var libro = ExcelDePrueba.Leer(bytes);

        libro.Hojas.Keys.Should().Equal(Hojas);
        libro.Hojas.Values.Should().OnlyContain(h => h[0][0] != "Placa", "una sola placa: sin columna Placa");
        var simit = libro.Hojas["SIMIT"];
        simit.Skip(1).Should().Contain(f => f.Contains("450000.00"));
        CeldaInline(bytes, "SIMIT", "450000.00").Should().BeTrue("el valor del SIMIT es texto en línea, nunca número (SP-21)");
        CeldaNumero(bytes, "Impuesto", "1250000").Should().BeTrue("el impuesto es número");
        libro.Texto.Should().Contain("****3040");
        libro.Texto.Should().NotContain(DeudaDePrueba.DocumentoPropietario, "ninguna celda trae el documento completo (SP-11)");
        libro.Hojas["Saneamiento"].Skip(1).Should().Contain(f => f.Contains("Nota de prueba"));
        libro.Hojas["Resumen"].Skip(1).Select(f => f[0]).Should().Contain(["Dictamen", "SIMIT_PROPIETARIO", "IMPUESTO", "SOAT", "RTM"]);
    }

    // SP-21 — un estado =HYPERLINK(...), un número @SUM(1) y un valor +1200 quedan como texto literal; ningún <f>, ni vínculos.
    [Fact]
    public void Texto_que_parece_formula_queda_literal_y_no_hay_formulas_ni_vinculos()
    {
        var bytes = ExcelDeuda.Generar([ConFormulas()], conPlaca: false);
        var libro = ExcelDePrueba.Leer(bytes);

        var fila = libro.Hojas["SIMIT"].FindIndex(f => f.Contains("@SUM(1)"));
        fila.Should().BePositive();
        libro.ComoTextoLiteral("SIMIT", fila, "Número").Should().BeTrue();
        libro.ComoTextoLiteral("SIMIT", fila, "Estado").Should().BeTrue();
        libro.Formulas.Should().Be(0);
        var xml = XmlDelLibro(bytes);
        ElementoFormula().IsMatch(xml).Should().BeFalse("ningún elemento <f> en el libro");
        Vinculo().IsMatch(xml).Should().BeFalse("sin elementos <hyperlink> ni relaciones de vínculo");
        CeldaInline(bytes, "SIMIT", "+1200").Should().BeTrue("el valor del SIMIT nunca se convierte a número");
    }

    // AC38 — varias placas: las mismas hojas con Placa al inicio y la fila «Omitidos: n» en Resumen.
    [Fact]
    public void Libro_de_varias_placas_lleva_placa_al_inicio_y_omitidos()
    {
        var bytes = ExcelDeuda.Generar([PdfDeudaTests.Panel("TST741"), PdfDeudaTests.Panel("TST742")], conPlaca: true, omitidos: 3);
        var libro = ExcelDePrueba.Leer(bytes);

        libro.Hojas.Keys.Should().Equal(Hojas);
        libro.Hojas.Values.Should().OnlyContain(h => h[0][0] == "Placa");
        libro.Hojas["Resumen"].Skip(1).Select(f => f[0]).Should().Contain(["TST741", "TST742"]);
        libro.Texto.Should().Contain("Omitidos: 3");
    }

    // AC38/SP-9/SP-18 por HTTP — Excel de una placa con cabeceras seguras; varias placas: lista vacía, 201 ids o un elemento que no
    // es uuid dan 400 LISTA_INVALIDA; 200 distintos más repetidos se aceptan; con PROPIOS el lote ajeno y el de otra compañía se
    // omiten sin revelarse; sin reportes.descargar 403.
    [Fact]
    public async Task Http_excel_de_una_y_varias_placas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ListadoDePrueba.ArrancarAsync(new UsuariosIdentidadDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        var creador = DeudaDePrueba.TokenDescargar(api, empresas.A.Id);
        var otro = DeudaDePrueba.TokenDescargar(api, empresas.A.Id);
        var simit = """{"semaforo":"ROJO","cantidad_comparendos":1,"comparendos":[{"numero":"TST-C-0751","valor":"77000","estado":"PENDIENTE"}]}""";
        async Task<Tablero.LoteSembrado> LoteAsync(EmpresaDePrueba empresa, string placa, Guid? dueno)
        {
            var lote = await TableroAyudas.SembrarAsync(empresa, ct, creador: dueno, 
                vehiculos: [new VehiculoSembrado(placa, [new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1)])]);
            await TableroAyudas.PayloadAsync(empresa, lote.Celda(placa, Fuente.SimitVehiculo), simit, ct);
            return lote;
        }

        var propio = await LoteAsync(empresas.A, "TST751", TableroAyudas.Usuario(otro));
        var ajeno = await LoteAsync(empresas.A, "TST752", TableroAyudas.Usuario(creador));
        var deB = await LoteAsync(empresas.B, "TST753", null);

        var una = await api.Cliente(otro).GetAsync(DeudaDePrueba.RutaExcel(propio.Id, propio.Vehiculos.Single().Id), ct);
        una.StatusCode.Should().Be(HttpStatusCode.OK, await ExcelDePrueba.CuerpoSiNoEsExcel(una, ct));
        una.Content.Headers.ContentType!.MediaType.Should().Be(ExcelDePrueba.ContentType);
        una.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be("deuda-TST751.xlsx");
        DeudaDePrueba.CabecerasSeguras(una);

        (await PostAsync(api, otro, """{"vehiculo_ids":[]}""", ct)).Should().Be("LISTA_INVALIDA");
        (await PostAsync(api, otro, $$"""{"vehiculo_ids":[{{string.Join(',', Enumerable.Range(0, 201).Select(_ => $"\"{Guid.NewGuid()}\""))}}]}""", ct)).Should().Be("LISTA_INVALIDA");
        (await PostAsync(api, otro, """{"vehiculo_ids":["no-es-uuid"]}""", ct)).Should().Be("LISTA_INVALIDA");
        (await PostAsync(api, otro, "no es json", ct)).Should().Be("LISTA_INVALIDA");
        var doscientos = Enumerable.Range(0, 199).Select(_ => Guid.NewGuid()).Append(propio.Vehiculos.Single().Id).ToList();
        var conRepetidos = doscientos.Concat(doscientos.Take(10)).Select(g => $"\"{g}\"");
        (await PostAsync(api, otro, $$"""{"vehiculo_ids":[{{string.Join(',', conRepetidos)}}]}""", ct)).Should().Be("OK", "200 distintos más 10 repetidos");

        await DeudaDePrueba.PropiosAsync(empresas.A, ct);
        var ids = new[] { propio.Vehiculos.Single().Id, ajeno.Vehiculos.Single().Id, deB.Vehiculos.Single().Id };
        var varios = await api.Cliente(otro).PostAsJsonAsync(DeudaDePrueba.RutaExcelVarios, new { vehiculo_ids = ids }, ct);
        varios.StatusCode.Should().Be(HttpStatusCode.OK, await ExcelDePrueba.CuerpoSiNoEsExcel(varios, ct));
        DeudaDePrueba.CabecerasSeguras(varios);
        var libro = ExcelDePrueba.Leer(await varios.Content.ReadAsByteArrayAsync(ct));
        libro.Texto.Should().Contain("TST751").And.Contain("Omitidos: 2");
        libro.Texto.Should().NotContainAny(["TST752", "TST753", ajeno.Vehiculos.Single().Id.ToString(), deB.Vehiculos.Single().Id.ToString()]);

        var soloVer = DeudaDePrueba.TokenVer(api, empresas.A.Id);
        (await api.Cliente(soloVer).PostAsJsonAsync(DeudaDePrueba.RutaExcelVarios, new { vehiculo_ids = ids }, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.Cliente(soloVer).GetAsync(DeudaDePrueba.RutaExcel(propio.Id, propio.Vehiculos.Single().Id), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>«OK» si respondió 200 con un .xlsx; si no, el <c>code</c> del error.</summary>
    private static async Task<string> PostAsync(Api.ApiDePrueba api, string token, string cuerpo, CancellationToken ct)
    {
        using var contenido = new StringContent(cuerpo, Encoding.UTF8, "application/json");
        var respuesta = await api.Cliente(token).PostAsync(DeudaDePrueba.RutaExcelVarios, contenido, ct);
        if (respuesta.StatusCode == HttpStatusCode.OK && respuesta.Content.Headers.ContentType?.MediaType == ExcelDePrueba.ContentType)
            return "OK";
        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await respuesta.Content.ReadAsStringAsync(ct));
        return (await DeudaDePrueba.JsonAsync(respuesta, ct)).GetProperty("code").GetString()!;
    }

    /// <summary>Todo el XML del paquete (hojas, estilos, relaciones) concatenado.</summary>
    private static string XmlDelLibro(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        var texto = new StringBuilder();
        foreach (var entrada in zip.Entries)
        {
            using var lector = new StreamReader(entrada.Open());
            texto.Append(entrada.FullName).Append('\n').Append(lector.ReadToEnd()).Append('\n');
        }

        return texto.ToString();
    }

    /// <summary>Celdas de la hoja con el texto dado, leídas con OpenXml (tipo y valor tal como quedaron en el paquete).</summary>
    private static List<DocumentFormat.OpenXml.Spreadsheet.Cell> Celdas(byte[] xlsx, string hoja, string valor)
    {
        using var documento = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(new MemoryStream(xlsx), false);
        var libro = documento.WorkbookPart!;
        var id = libro.Workbook.Sheets!.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().Single(h => h.Name == hoja).Id!;
        var parte = (DocumentFormat.OpenXml.Packaging.WorksheetPart)libro.GetPartById(id!);
        return [.. parte.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>()
            .Where(c => (c.InlineString?.InnerText ?? c.CellValue?.Text) == valor)
            .Select(c => (DocumentFormat.OpenXml.Spreadsheet.Cell)c.CloneNode(true))];
    }

    private static bool CeldaInline(byte[] xlsx, string hoja, string valor)
    {
        var celdas = Celdas(xlsx, hoja, valor);
        return celdas.Count > 0 && celdas.All(c => c.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString);
    }

    private static bool CeldaNumero(byte[] xlsx, string hoja, string valor)
    {
        var celdas = Celdas(xlsx, hoja, valor);
        return celdas.Count > 0 && celdas.All(c => c.DataType is null && c.CellValue is not null);
    }

    [GeneratedRegex(@"<(\w+:)?hyperlinks?[\s>/]|relationships/hyperlink")]
    private static partial Regex Vinculo();

    [GeneratedRegex(@"<(\w+:)?f[\s>/]")]
    private static partial Regex ElementoFormula();
}
