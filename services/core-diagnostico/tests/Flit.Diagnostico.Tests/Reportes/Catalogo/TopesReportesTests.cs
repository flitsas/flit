using System.Diagnostics;
using System.Net;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    // CR-BE-11 — el borde del PDF: 1 000 filas caben (prueba de arriba) y 1 001 ya es 422, sin descifrar ni registrar.
    [Fact]
    public async Task BE3_pdf_de_1001_filas_es_422_sin_descifrar_ni_registrar()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteGrandeAsync(empresa, 1001, ct, conPropietario: true);
        var protector = ReportesDePrueba.Protector(api);
        protector.Reiniciar();

        var respuesta = await api.Cliente(ReportesDePrueba.Token(api, empresa.Id, descargar: true))
            .GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/pdf"), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cuerpo = await ReportesDePrueba.JsonAsync(respuesta, ct);
        cuerpo.GetProperty("code").GetString().Should().Be("REPORTE_DEMASIADO_GRANDE");
        cuerpo.GetProperty("filas").GetInt32().Should().Be(1001);
        cuerpo.GetProperty("maximo").GetInt32().Should().Be(1000);
        protector.Llamadas.Should().Be(0);
        (await ReportesDePrueba.AccesosAsync(empresa, ct)).Should().BeEmpty("un 422 no audita");
    }

    // CR-BE-7 — si las filas crecen entre el conteo y la lectura, el archivo lee como mucho máximo + 1 (nunca todas) y responde 422
    // con esa cifra, sin registrar el acceso.
    [Fact]
    public async Task BE3_archivo_lee_como_mucho_maximo_mas_uno_si_las_filas_crecen_tras_contar()
    {
        var ct = TestContext.Current.CancellationToken;
        var reporte = new ReporteQueCrece(Contadas: 3, Disponibles: 50);
        await using var db = DosEmpresas.Contexto("Host=127.0.0.1;Database=solo_modelo", null);
        var motor = new MotorReportes(
            [reporte], new DescifradoDocumentos(null!, NullLogger<DescifradoDocumentos>.Instance),
            Options.Create(new OpcionesReportes { MaximoFilasPdf = 10 }), db, NullLogger<MotorReportes>.Instance);
        var filtro = new FiltroReporte(TipoReporte.Vehiculos, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 10), new Dictionary<string, IReadOnlyList<string>>(), null, 1, 25);
        var auditado = false;

        var error = (await FluentActions.Awaiting(() => motor.ArchivoAsync(
                new ContextoReporte(AlcanceReporte.Compania, null!), filtro, FormatoReporte.Pdf,
                (_, _, _) =>
                {
                    auditado = true;
                    return Task.FromResult(Guid.NewGuid());
                },
                DateTimeOffset.UnixEpoch, ct))
            .Should().ThrowAsync<ReporteDemasiadoGrandeException>()).Which;

        reporte.Tomado.Should().Be(11, "basta una fila de más para saber que pasa el máximo");
        (error.Filas, error.Maximo).Should().Be((11, 10));
        auditado.Should().BeFalse("un 422 no audita");
    }

    /// <summary>Reporte que cuenta <c>Contadas</c> filas pero al leer tiene <c>Disponibles</c> (entraron filas entre las dos consultas).</summary>
    private sealed record ReporteQueCrece(int Contadas, int Disponibles) : IReporte
    {
        public int? Tomado { get; private set; }

        public TipoReporte Tipo => TipoReporte.Vehiculos;

        public Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct) => Task.FromResult(Contadas);

        public Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct)
        {
            Tomado = tomar;
            IReadOnlyList<FilaReporte> filas = [.. Enumerable.Range(0, Math.Min(Disponibles, tomar ?? Disponibles)).Select(_ => new FilaReporte { Empresa = Guid.Empty })];
            return Task.FromResult(filas);
        }
    }
}
