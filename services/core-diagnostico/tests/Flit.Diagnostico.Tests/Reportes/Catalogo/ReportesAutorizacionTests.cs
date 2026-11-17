using System.Net;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-2 (SP-1, O8; contrato-api §2) — parte de la compañía: el catálogo (R0) y la pantalla (R1) exigen
/// <c>diagnostico.reportes.ver</c>; el catálogo trae los 4 tipos sin consumo, con filtros, <c>contiene_pii</c>, fecha de referencia y
/// los máximos de la configuración. La parte del backoffice la suma BE-4. PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ReportesAutorizacionTests
/// </code>
/// </example>
public sealed class ReportesAutorizacionTests
{
    [Fact]
    public async Task BE2_catalogo_y_pantalla_exigen_reportes_ver()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync(new Dictionary<string, string?> { ["Diagnostico:Reportes:MaximoFilasExcel"] = "4000" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        using var sinPermiso = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, ver: false, descargar: true));
        using var conPermiso = api.Cliente(ReportesDePrueba.Token(api, empresa.Id));

        (await sinPermiso.GetAsync(ReportesDePrueba.Base, ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinPermiso.GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await sinPermiso.GetAsync(ReportesDePrueba.Ruta("desconocido"), ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "el permiso va antes que el tipo");

        var catalogo = await conPermiso.GetAsync(ReportesDePrueba.Base, ct);
        catalogo.StatusCode.Should().Be(HttpStatusCode.OK, await catalogo.Content.ReadAsStringAsync(ct));
        catalogo.Headers.CacheControl!.NoStore.Should().BeTrue();
        var reportes = (await ReportesDePrueba.JsonAsync(catalogo, ct)).GetProperty("reportes").EnumerateArray().ToList();
        reportes.Select(r => r.GetProperty("tipo").GetString()).Should().Equal("vehiculos", "consultas", "saneamientos", "derivaciones");
        foreach (var r in reportes)
        {
            r.EnumerateObject().Select(p => p.Name).Should().Equal("tipo", "nombre", "descripcion", "contiene_pii", "fecha_referencia", "filtros", "maximo_excel", "maximo_pdf");
            r.GetProperty("maximo_excel").GetInt32().Should().Be(4000, "los máximos salen de la configuración");
            r.GetProperty("maximo_pdf").GetInt32().Should().Be(1000);
            r.GetProperty("filtros").EnumerateArray().Select(f => f.GetProperty("clave").GetString()).Should().NotContain(["desde", "hasta", "documento", "nombre"]);
        }

        reportes.Where(r => r.GetProperty("contiene_pii").GetBoolean()).Select(r => r.GetProperty("tipo").GetString()).Should().Equal("vehiculos");
        reportes[0].GetProperty("filtros").EnumerateArray().Select(f => f.GetProperty("clave").GetString())
            .Should().Equal("modalidad", "tipo_analisis", "veredicto", "placa");
        var placa = reportes[0].GetProperty("filtros").EnumerateArray().Last();
        placa.GetProperty("tipo").GetString().Should().Be("prefijo");
        placa.GetProperty("valores").GetArrayLength().Should().Be(0);
        var fuente = reportes[1].GetProperty("filtros").EnumerateArray().First();
        fuente.GetProperty("clave").GetString().Should().Be("fuente");
        fuente.GetProperty("repetible").GetBoolean().Should().BeTrue();
        fuente.GetProperty("valores").EnumerateArray().Select(v => v.GetString()).Should().HaveCount(8).And.Contain("IMPUESTO_DEPARTAMENTAL");
        reportes[2].GetProperty("filtros").EnumerateArray().First().GetProperty("valores").EnumerateArray().Select(v => v.GetString())
            .Should().Equal("SOLICITADO", "EN_GESTION", "SANEADO", "NO_PROCEDE", "ANULADO");

        (await conPermiso.GetAsync(ReportesDePrueba.Ruta("vehiculos"), ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
