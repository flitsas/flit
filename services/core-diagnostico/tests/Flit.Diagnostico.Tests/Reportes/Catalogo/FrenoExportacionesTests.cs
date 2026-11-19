using System.Net;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Tests.Aislamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes.Catalogo;

/// <summary>
/// HU #14579, BE-3 (SP-12; contrato-api §5) — freno de concurrencia de exportaciones: una exportación en curso por usuario (<c>sub</c>) y
/// un tope por proceso; el exceso responde 429 <c>EXPORTACION_EN_CURSO</c> con <c>Retry-After: 10</c> y no deja registro. La exportación
/// retenida es un descifrado lento (compuerta del protector de prueba).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~FrenoExportacionesTests
/// </code>
/// </example>
public sealed class FrenoExportacionesTests
{
    [Fact]
    public async Task BE3_una_exportacion_por_usuario_y_otro_usuario_sigue()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT801", Propietarios: [new("CC", "1231231231")])], ct);
        var protector = ReportesDePrueba.Protector(api);
        using var compuerta = new SemaphoreSlim(0);
        protector.Compuerta = compuerta;
        var u = Guid.NewGuid();
        using var deU = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, u, descargar: true));
        using var otraDeU = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, u, descargar: true));
        using var deV = api.Cliente(ReportesDePrueba.Token(api, empresa.Id, Guid.NewGuid(), descargar: true));

        var lenta = deU.GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/excel"), ct);
        await protector.PrimerDescifrado.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);

        var segunda = await otraDeU.GetAsync(ReportesDePrueba.Ruta("consultas", sufijo: "/pdf"), ct);
        var deOtro = await deV.GetAsync(ReportesDePrueba.Ruta("consultas", sufijo: "/excel"), ct);
        compuerta.Release(100);
        var primera = await lenta;

        segunda.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await ReportesDePrueba.CodigoAsync(segunda, ct)).Should().Be("EXPORTACION_EN_CURSO");
        segunda.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(10));
        deOtro.StatusCode.Should().Be(HttpStatusCode.OK, "otro usuario exporta al mismo tiempo");
        primera.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReportesDePrueba.AccesosAsync(empresa, ct)).Should().HaveCount(2, "el 429 no audita").And.OnlyContain(a => a.Formato == FormatoReporte.Xlsx);

        var despues = await otraDeU.GetAsync(ReportesDePrueba.Ruta("consultas", sufijo: "/pdf"), ct);
        despues.StatusCode.Should().Be(HttpStatusCode.OK, "al terminar la primera, el usuario vuelve a exportar");
    }

    [Fact]
    public async Task BE3_tope_por_proceso()
    {
        await using var api = await ReportesDePrueba.ArrancarAsync(new Dictionary<string, string?> { ["Diagnostico:Reportes:ExportacionesPorProceso"] = "2" });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await ReportesDePrueba.LoteAsync(empresa, [new("RPT811", Propietarios: [new("CC", "4564564564")])], ct);
        var protector = ReportesDePrueba.Protector(api);
        using var compuerta = new SemaphoreSlim(0);
        protector.Compuerta = compuerta;
        HttpClient Cliente() => api.Cliente(ReportesDePrueba.Token(api, empresa.Id, Guid.NewGuid(), descargar: true));
        using var c1 = Cliente();
        using var c2 = Cliente();
        using var c3 = Cliente();

        var r1 = c1.GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/excel"), ct);
        var r2 = c2.GetAsync(ReportesDePrueba.Ruta("vehiculos", sufijo: "/pdf"), ct);
        var espera = DateTime.UtcNow.AddSeconds(30);
        while (protector.Llamadas < 2 && DateTime.UtcNow < espera)
            await Task.Delay(50, ct);
        var tercera = await c3.GetAsync(ReportesDePrueba.Ruta("consultas", sufijo: "/excel"), ct);
        compuerta.Release(100);

        protector.Llamadas.Should().BeGreaterThanOrEqualTo(2, "las dos primeras están retenidas en el descifrado");
        tercera.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "el proceso ya tiene sus 2 exportaciones");
        (await ReportesDePrueba.CodigoAsync(tercera, ct)).Should().Be("EXPORTACION_EN_CURSO");
        (await r1).StatusCode.Should().Be(HttpStatusCode.OK);
        (await r2).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
