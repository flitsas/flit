using System.Net;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Liquidaciones;
using Flit.Impuestos.Tests.Reportes;
using FluentAssertions;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Aislamiento;

/// <summary>
/// HU #14694 (M14; plan §5.1, F2a-CF9) — aislamiento del dashboard, los cuatro reportes, sus exportes y los catálogos de reportes:
/// con el token de una compañía nunca aparece una placa, un monto ni un creador de otra, ni filtrando por el usuario de la otra.
/// El Super Admin con <c>empresa</c> ve solo esa compañía.
/// </summary>
/// <example>
/// <code>
/// var texto = await cliente.GetStringAsync("/api/v1/impuestos/reportes/detalle", ct); // sin placas de otra compañía
/// </code>
/// </example>
public sealed class AislamientoReportesTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task Ninguna_lectura_de_una_compania_trae_datos_de_otra()
    {
        var ct = Preparar();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var creadorB = Guid.NewGuid();
        await E.SembrarEmpresaAsync(Api, a, ct);
        await E.SembrarEmpresaAsync(Api, b, ct);
        await E.SembrarAsync(Api, a, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("AIS001", ResultadoStatus: "con_deuda", Total: 10m, PropietarioNombre: "Dueño A", DocumentoTipo: "CC", DocumentoNumero: "111"),
            new ItemSemilla("AIS002", Flit.Impuestos.Domain.Liquidaciones.EstadoItem.Error, ErrorCodigo: "portal_unavailable"));
        await E.SembrarAsync(Api, b, TipoLiquidacion.Masiva, creadorB, ct, null,
            new ItemSemilla("OTR001", ResultadoStatus: "con_deuda", Total: 7777.77m, PropietarioNombre: "Dueño B", DocumentoTipo: "CC", DocumentoNumero: "999"),
            new ItemSemilla("OTR002", Flit.Impuestos.Domain.Liquidaciones.EstadoItem.Error, ErrorCodigo: "portal_unavailable"));
        var cliente = Api.Cliente(Api.Token(empresa: a, permisos: [.. E.Ver, .. E.Exportar]));

        var textos = new List<string>();
        foreach (var ruta in new[]
                 {
                     E.Dashboard, $"{E.Reportes}/detalle", $"{E.Reportes}/lotes", $"{E.Reportes}/deuda", $"{E.Reportes}/errores", E.Catalogos,
                     $"{E.Reportes}/detalle?usuario={creadorB}",
                 })
        {
            using var r = await cliente.GetAsync(ruta, ct);
            var texto = await r.Content.ReadAsStringAsync(ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"{ruta}: {texto}");
            textos.Add(EntornoReportes.TextoDe(texto));
        }

        foreach (var formato in new[] { "excel", "pdf" })
        {
            using var r = await cliente.GetAsync($"{E.Reportes}/detalle/{formato}", ct);
            r.StatusCode.Should().Be(HttpStatusCode.OK);
            if (formato == "excel")
                textos.Add(string.Join("|", EntornoMasiva.LeerXlsx(await r.Content.ReadAsByteArrayAsync(ct)).Filas.SelectMany(f => f)));
        }

        textos.Should().OnlyContain(t => !t.Contains("OTR00") && !t.Contains("Dueño B") && !t.Contains("7777.77") && !t.Contains(b.ToString())
            && !t.Contains(creadorB.ToString()));
        textos[0].Should().Contain("montoTotal|10");
        textos[1].Should().Contain("AIS001");
        textos[6].Should().Contain("total|0|", "filtrar por el usuario de otra compañía no trae nada");

        var sa = await E.GetJsonAsync(Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT")), $"{E.Reportes}/detalle?empresa={a}", ct);
        E.Filas(sa).Select(f => f.GetProperty("placa").GetString()).Should().BeEquivalentTo(["AIS001", "AIS002"]);
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        return TestContext.Current.CancellationToken;
    }
}
