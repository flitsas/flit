using System.Diagnostics;
using System.Net;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-3 (SP-11; contrato-api §5) — topes sin recorte silencioso, con los valores por defecto: Excel hasta 5 000 filas y PDF
/// hasta 1 000. Por encima, 422 <c>REPORTE_DEMASIADO_GRANDE</c> con <c>filas</c> y <c>maximo</c>, sin descifrar ningún documento ni dejar
/// registro. Mide además el tiempo de exportar 5 000 filas con un propietario cifrado cada una (riesgo de CPU del descifrado).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~TopesReportesTests
/// </code>
/// </example>
public sealed class TopesReportesTests
{
    [Fact]
    public async Task BE3_topes_de_excel_y_pdf_sin_recorte_silencioso()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteGrandeAsync(empresa, 5000, ct, conPropietario: true);
        var protector = ReportesDePrueba.Protector(api);
        using var cliente = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, descargar: true));

        protector.Reiniciar();
        var reloj = Stopwatch.StartNew();
        var cinco = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/excel"), ct);
        var bytes = await cinco.Content.ReadAsByteArrayAsync(ct);
        reloj.Stop();
        cinco.StatusCode.Should().Be(HttpStatusCode.OK, System.Text.Encoding.UTF8.GetString(bytes[..Math.Min(400, bytes.Length)]));
        protector.Llamadas.Should().Be(5000, "un descifrado por documento");
        ExcelDePrueba.Leer(bytes).Hojas["Reporte"].Should().HaveCount(5002, "aviso, encabezados y las 5 000 filas: nada se recorta");
        TestContext.Current.TestOutputHelper?.WriteLine($"MEDICION exportar 5000 filas con documento a Excel: {reloj.ElapsedMilliseconds} ms, {bytes.Length} bytes");

        reloj.Restart();
        var mil = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", "placa=RPT00", "/pdf"), ct);
        var pdf = await mil.Content.ReadAsByteArrayAsync(ct);
        reloj.Stop();
        mil.StatusCode.Should().Be(HttpStatusCode.OK, "1 000 filas caben en el PDF");
        TestContext.Current.TestOutputHelper?.WriteLine($"MEDICION exportar 1000 filas con documento a PDF: {reloj.ElapsedMilliseconds} ms, {pdf.Length} bytes");

        await ReportesDePrueba.LoteGrandeAsync(empresa, 1, ct, conPropietario: true, prefijo: "RPU");
        var accesosAntes = (await ReportesDePrueba.AccesosAsync(empresa, ct)).Count;
        protector.Reiniciar();

        var excel = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/excel"), ct);
        var pdfGrande = await cliente.GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/pdf"), ct);

        excel.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cuerpo = await ReportesDePrueba.JsonAsync(excel, ct);
        cuerpo.GetProperty("code").GetString().Should().Be("REPORTE_DEMASIADO_GRANDE");
        cuerpo.GetProperty("filas").GetInt32().Should().Be(5001);
        cuerpo.GetProperty("maximo").GetInt32().Should().Be(5000);
        cuerpo.GetProperty("error").GetString().Should().Be("El reporte tiene más filas de las que admite el formato. Acota el rango o los filtros.");
        pdfGrande.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReportesDePrueba.JsonAsync(pdfGrande, ct)).GetProperty("maximo").GetInt32().Should().Be(1000);
        protector.Llamadas.Should().Be(0, "el conteo va antes de descifrar");
        (await ReportesDePrueba.AccesosAsync(empresa, ct)).Should().HaveCount(accesosAntes, "un 422 no audita");
    }
}
