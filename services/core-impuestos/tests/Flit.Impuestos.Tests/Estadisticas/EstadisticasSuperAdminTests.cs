using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Reportes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using E = Flit.Impuestos.Tests.Reportes.EntornoReportes;

namespace Flit.Impuestos.Tests.Estadisticas;

/// <summary>
/// HU #14694 (M14; F4-CF5) — vista global y ranking solo del Super Admin: sin <c>empresa</c> ve la suma de todas las compañías con
/// <c>ranking[]</c> (nombre del directorio de la plataforma, la de más liquidaciones primero) y cada compañía leída queda auditada;
/// con <c>empresa</c> ve la de esa compañía sin ranking. Quien no es Super Admin y envía <c>empresa</c> recibe 403
/// <c>EMPRESA_SOLO_SUPER_ADMIN</c>; el Super Admin con un valor que no es uuid, 400 <c>EMPRESA_REQUERIDA</c>; con una compañía sin
/// Impuestos, 404 <c>EMPRESA_NO_ENCONTRADA</c>.
/// </summary>
/// <example>
/// <code>
/// var global = await EntornoReportes.GetJsonAsync(api.Cliente(api.Token(superAdmin: true)), "/api/v1/impuestos/dashboard", ct);
/// </code>
/// </example>
public sealed class EstadisticasSuperAdminTests(ApiReportesFixture fixture) : IClassFixture<ApiReportesFixture>
{
    private ApiDePrueba Api => fixture.Api;

    [Fact]
    public async Task El_Super_Admin_sin_empresa_ve_el_global_con_ranking_y_cada_compania_queda_auditada()
    {
        var ct = Preparar();
        var (a, b) = await DosEmpresasAsync(ct);

        var d = await E.GetJsonAsync(Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT")), E.Dashboard, ct);

        var ranking = d.GetProperty("ranking").EnumerateArray().ToList();
        var deA = ranking.Single(r => r.GetProperty("empresa").GetProperty("id").GetGuid() == a);
        var deB = ranking.Single(r => r.GetProperty("empresa").GetProperty("id").GetGuid() == b);
        deA.GetProperty("total").GetInt32().Should().Be(3);
        deA.GetProperty("empresa").GetProperty("nombre").GetString().Should().Be("Flota Andina SAS");
        deB.GetProperty("total").GetInt32().Should().Be(1);
        ranking.IndexOf(deA).Should().BeLessThan(ranking.IndexOf(deB), "la de más liquidaciones primero");
        d.GetProperty("contadores").GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(4, "suma todas las compañías");
        d.GetProperty("ultimosLotes").EnumerateArray().Select(l => l.GetProperty("empresa").GetProperty("id").GetGuid()).Should().Contain([a, b]);
        foreach (var empresa in new[] { a, b })
        {
            await using var db = Api.Contexto(empresa);
            (await db.Set<LecturaBackoffice>().CountAsync(l => l.Recurso == "dashboard", ct)).Should().BeGreaterThan(0, "la lectura global queda auditada");
        }
    }

    [Fact]
    public async Task El_Super_Admin_con_empresa_ve_solo_esa_compania_sin_ranking()
    {
        var ct = Preparar();
        var (a, _) = await DosEmpresasAsync(ct);

        var d = await E.GetJsonAsync(Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT")), $"{E.Dashboard}?empresa={a}", ct);

        d.GetProperty("contadores").GetProperty("total").GetInt32().Should().Be(3);
        d.GetProperty("ranking").ValueKind.Should().Be(JsonValueKind.Null);
        d.GetProperty("ultimosLotes").EnumerateArray().Should().OnlyContain(l => l.GetProperty("empresa").GetProperty("id").GetGuid() == a);
    }

    [Fact]
    public async Task La_regla_de_empresa_es_la_unica_del_contrato()
    {
        var ct = Preparar();
        var (a, _) = await DosEmpresasAsync(ct);
        var sa = Api.Cliente(Api.Token(superAdmin: true, tipoEmpresa: "FLIT"));
        var gestor = Api.Cliente(Api.Token(empresa: a, permisos: E.Ver));

        await LiquidacionIndividualEndpointsTests.EsperarError(await gestor.GetAsync($"{E.Dashboard}?empresa={a}", ct),
            HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync($"{E.Dashboard}?empresa=no-es-uuid", ct),
            HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync($"{E.Dashboard}?empresa={Guid.NewGuid()}", ct),
            HttpStatusCode.NotFound, "EMPRESA_NO_ENCONTRADA", "La compañía no existe o no tiene Impuestos.");
        (await E.GetJsonAsync(gestor, E.Dashboard, ct)).GetProperty("ranking").ValueKind.Should().Be(JsonValueKind.Null, "el ranking es solo del Super Admin");
    }

    /// <summary>Dos compañías con nombre en el directorio: A con 3 placas (una masiva) y B con 1 (individual).</summary>
    private async Task<(Guid A, Guid B)> DosEmpresasAsync(CancellationToken ct)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        fixture.Directorio.Sembrar(a, "Flota Andina SAS", "900123456");
        fixture.Directorio.Sembrar(b, "Rentacar del Sur", "900654321");
        await E.SembrarEmpresaAsync(Api, a, ct);
        await E.SembrarEmpresaAsync(Api, b, ct);
        await E.SembrarAsync(Api, a, TipoLiquidacion.Masiva, Guid.NewGuid(), ct, null,
            new ItemSemilla("GGG001", ResultadoStatus: "con_deuda", Total: 1m), new ItemSemilla("GGG002", ResultadoStatus: "con_deuda", Total: 2m),
            new ItemSemilla("GGG003", ResultadoStatus: "con_deuda", Total: 3m));
        await E.SembrarAsync(Api, b, TipoLiquidacion.Individual, Guid.NewGuid(), ct, null, new ItemSemilla("HHH001", ResultadoStatus: "paz_y_salvo", Total: 0m));
        return (a, b);
    }

    private CancellationToken Preparar()
    {
        Assert.SkipWhen(Api.Omitir is not null, Api.Omitir ?? string.Empty);
        return TestContext.Current.CancellationToken;
    }
}
