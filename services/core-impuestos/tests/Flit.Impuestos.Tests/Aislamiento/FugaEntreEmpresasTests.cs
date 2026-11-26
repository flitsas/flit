using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Impuestos.Tests.Aislamiento;

/// <summary>
/// HU #14694 (M09; F2a-CF9/CF10, ADR-0072) — una compañía nunca ve ni toca las liquidaciones de otra: el grid no las lista, el
/// detalle y sus placas responden 404 <c>LIQUIDACION_NO_ENCONTRADA</c> como si no existieran, <c>?empresa</c> de quien no es Super
/// Admin es 403, y el Super Admin ve solo la compañía que eligió (con la lectura auditada). Un sobre del bus con otra empresa no
/// procesa el ítem ajeno ni su despachador libera crédito ajeno.
/// </summary>
/// <example>
/// <code>
/// var r = await api.Cliente(api.Token(empresa: otra, permisos: [Permisos.LiquidacionesVer])).GetAsync($"{Lista}/{lote}");
/// // 404 LIQUIDACION_NO_ENCONTRADA
/// </code>
/// </example>
public sealed class FugaEntreEmpresasTests
{
    private const string Lista = Program.Prefijo + "/liquidaciones";

    [Fact]
    public async Task Otra_compania_no_ve_el_grid_ni_el_detalle_ni_las_placas()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var duena = Guid.NewGuid();
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, duena, ct);
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);
        var creada = await api.Cliente(api.Token(empresa: duena, permisos: [Permisos.LiquidacionesVer, Permisos.LiquidacionesCrear]))
            .PostAsJsonAsync(Lista + "/individual", new { placa = "ABC123" }, ct);
        creada.StatusCode.Should().Be(HttpStatusCode.Created);
        var lote = JsonElement.Parse(await creada.Content.ReadAsStringAsync(ct)).GetProperty("id").GetGuid();
        var ajena = api.Cliente(api.Token(empresa: otra, permisos: [Permisos.LiquidacionesVer]));

        using (var pagina = JsonDocument.Parse(await ajena.GetStringAsync(Lista, ct)))
        {
            pagina.RootElement.GetProperty("total").GetInt32().Should().Be(0);
            pagina.RootElement.GetProperty("items").GetArrayLength().Should().Be(0);
        }

        await LiquidacionIndividualEndpointsTests.EsperarError(await ajena.GetAsync($"{Lista}/{lote}", ct),
            HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await ajena.GetAsync($"{Lista}/{lote}/items", ct),
            HttpStatusCode.NotFound, "LIQUIDACION_NO_ENCONTRADA", "La liquidación no existe.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await ajena.GetAsync($"{Lista}?empresa={duena}", ct),
            HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
        await LiquidacionIndividualEndpointsTests.EsperarError(await ajena.GetAsync($"{Lista}/{lote}?empresa={duena}", ct),
            HttpStatusCode.Forbidden, "EMPRESA_SOLO_SUPER_ADMIN", "Solo el Super Admin puede elegir la compañía.");
    }

    [Fact]
    public async Task El_Super_Admin_ve_solo_la_compania_que_elige_y_su_lectura_queda_auditada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var una = Guid.NewGuid();
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, una, ct);
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);
        var (loteUna, _) = await EntornoEjecucion.SembrarLoteAsync(api, una, ["UNA111"], ct);
        var (loteOtra, _) = await EntornoEjecucion.SembrarLoteAsync(api, otra, ["OTR222"], ct);
        var superAdmin = Guid.NewGuid();
        var sa = api.Cliente(api.Token(superAdmin: true, tipoEmpresa: "FLIT", usuario: superAdmin));

        await LiquidacionIndividualEndpointsTests.EsperarError(await sa.GetAsync(Lista, ct), HttpStatusCode.BadRequest, "EMPRESA_REQUERIDA", "Elige una compañía.");
        using (var pagina = JsonDocument.Parse(await sa.GetStringAsync($"{Lista}?empresa={una}", ct)))
        {
            var fila = pagina.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
            fila.GetProperty("id").GetGuid().Should().Be(loteUna);
            fila.GetProperty("empresa").GetProperty("id").GetGuid().Should().Be(una);
        }

        (await sa.GetAsync($"{Lista}/{loteOtra}?empresa={una}", ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, "el lote es de la otra compañía");
        (await sa.GetAsync($"{Lista}/{loteUna}?empresa={una}", ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = api.Contexto(una);
        (await db.Set<LecturaBackoffice>().AsNoTracking().Where(l => l.ActorId == superAdmin).CountAsync(ct)).Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task El_bus_no_procesa_ni_despacha_lo_de_otra_compania()
    {
        var ct = TestContext.Current.CancellationToken;
        var runt = new RuntDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(EntornoEjecucion.ProveedorQueAcepta(), runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var duena = Guid.NewGuid();
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, duena, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, duena, ["ABC123"], ct);

        (await EntornoEjecucion.DespacharAsync(api, otra, ct)).Should().Be(0);
        await EntornoEjecucion.EntregarAsync(api, EntornoEjecucion.Sobre(otra, lote, items[0]), ct);

        (await EntornoEjecucion.ItemAsync(api, duena, items[0], ct)).Estado.Should().Be(EstadoItem.Pendiente);
        runt.Llamadas.Should().BeEmpty();
    }
}
