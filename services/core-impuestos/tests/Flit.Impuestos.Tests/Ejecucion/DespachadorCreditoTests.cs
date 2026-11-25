using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Impuestos.Tests.Ejecucion;

/// <summary>
/// HU #14694 (M09; F3-CF10, DA-5, S-D) — el despachador libera el crédito: como mucho 2 ítems en vuelo (<c>CONSULTANDO_RUNT</c> o
/// <c>ENVIANDO</c>) por compañía y 6 en total. Cada ítem liberado pasa a <c>CONSULTANDO_RUNT</c>, su lote a <c>EN_PROCESO</c> y deja
/// <c>impuestos.liquidacion.solicitada</c> (solo ids) en la outbox, en la transacción de quien llama. La liquidación más antigua sale
/// primero. Una compañía no despacha los ítems de otra.
/// </summary>
/// <example>
/// <code>
/// var liberados = await EntornoEjecucion.DespacharAsync(api, empresa, ct);
/// </code>
/// </example>
public sealed class DespachadorCreditoTests
{
    [Theory]
    [InlineData(2, 0, 6, 0, 2)]
    [InlineData(2, 1, 6, 1, 1)]
    [InlineData(2, 2, 6, 2, 0)]
    [InlineData(2, 0, 6, 5, 1)]
    [InlineData(2, 0, 6, 6, 0)]
    [InlineData(2, 0, 6, 9, 0)]
    public void Libres_es_el_menor_hueco_entre_el_de_la_compania_y_el_global(int porEmpresa, int enVueloEmpresa, int global, int enVueloGlobal, int esperado) =>
        DespachadorCredito.Libres(porEmpresa, enVueloEmpresa, global, enVueloGlobal).Should().Be(esperado);

    [Fact]
    public void Los_valores_por_defecto_son_2_por_compania_y_6_en_total()
    {
        var opciones = new OpcionesEjecucion();

        opciones.ConcurrenciaPorEmpresa.Should().Be(2);
        opciones.ConcurrenciaGlobal.Should().Be(6);
        DespachadorCredito.EnVuelo.Should().BeEquivalentTo([EstadoItem.ConsultandoRunt, EstadoItem.Enviando]);
    }

    [Fact]
    public async Task Libera_dos_por_compania_marca_CONSULTANDO_RUNT_el_lote_EN_PROCESO_y_encola_solo_ids()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["AAA111", "BBB222", "CCC333", "DDD444", "EEE555"], ct);

        (await EntornoEjecucion.DespacharAsync(api, empresa, ct)).Should().Be(2);
        (await EntornoEjecucion.DespacharAsync(api, empresa, ct)).Should().Be(0, "ya hay 2 en vuelo");

        var estados = new List<EstadoItem>();
        foreach (var id in items)
            estados.Add((await EntornoEjecucion.ItemAsync(api, empresa, id, ct)).Estado);
        estados.Should().Equal(EstadoItem.ConsultandoRunt, EstadoItem.ConsultandoRunt, EstadoItem.Pendiente, EstadoItem.Pendiente, EstadoItem.Pendiente);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.EnProceso);

        var mensajes = await EntornoEjecucion.SolicitudesAsync(api, ct);
        mensajes.Should().HaveCount(2);
        foreach (var m in mensajes)
        {
            m.Exchange.Should().Be("flit.impuestos");
            using var sobre = JsonDocument.Parse(m.Payload);
            sobre.RootElement.GetProperty("tenantId").GetGuid().Should().Be(empresa);
            var data = sobre.RootElement.GetProperty("data");
            data.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["liquidacionId", "itemId"], "solo ids, sin placa ni documento");
            data.GetProperty("liquidacionId").GetGuid().Should().Be(lote);
        }
    }

    [Fact]
    public async Task Al_cerrar_un_item_en_vuelo_libera_el_siguiente()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["AAA111", "BBB222", "CCC333"], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await using (var db = api.Contexto(empresa))
        {
            var primero = await db.Set<LiquidacionItem>().SingleAsync(i => i.Id == items[0], ct);
            primero.Estado = EstadoItem.Enviado;
            await db.SaveChangesAsync(ct);
        }

        (await EntornoEjecucion.DespacharAsync(api, empresa, ct)).Should().Be(1, "ENVIADO ya no ocupa crédito");
        (await EntornoEjecucion.ItemAsync(api, empresa, items[2], ct)).Estado.Should().Be(EstadoItem.ConsultandoRunt);
    }

    [Fact]
    public async Task El_tope_global_es_6_entre_todas_las_companias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        foreach (var e in empresas)
            await EntornoEjecucion.SembrarLoteAsync(api, e, ["AAA111", "BBB222", "CCC333"], ct);

        var liberados = new List<int>();
        foreach (var e in empresas)
            liberados.Add(await EntornoEjecucion.DespacharAsync(api, e, ct));

        liberados.Should().Equal(2, 2, 2, 0);
        (await EntornoEjecucion.SolicitudesAsync(api, ct)).Should().HaveCount(6);
    }

    [Fact]
    public async Task La_liquidacion_mas_antigua_sale_primero_y_otra_compania_no_despacha_lo_ajeno()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        var otra = Guid.NewGuid();
        var (_, nuevos) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["NUE111", "NUE222"], ct, creadaEn: DateTimeOffset.UtcNow);
        var (_, viejos) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["VIE111", "VIE222"], ct, creadaEn: DateTimeOffset.UtcNow.AddHours(-1));
        var (_, ajenos) = await EntornoEjecucion.SembrarLoteAsync(api, otra, ["AJE111"], ct);

        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        (await EntornoEjecucion.ItemAsync(api, empresa, viejos[0], ct)).Estado.Should().Be(EstadoItem.ConsultandoRunt);
        (await EntornoEjecucion.ItemAsync(api, empresa, viejos[1], ct)).Estado.Should().Be(EstadoItem.ConsultandoRunt);
        (await EntornoEjecucion.ItemAsync(api, empresa, nuevos[0], ct)).Estado.Should().Be(EstadoItem.Pendiente);
        (await EntornoEjecucion.ItemAsync(api, otra, ajenos[0], ct)).Estado.Should().Be(EstadoItem.Pendiente);
    }

    [Fact]
    public async Task Fuera_de_una_transaccion_no_libera()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await EntornoEjecucion.ConApiAsync(new ApiLiquidacionDePrueba(), new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await using var scope = api.App.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Flit.Impuestos.Api.Plataforma.EmpresaDelTrabajo>().Fijar(empresa);

        var liberar = () => scope.ServiceProvider.GetRequiredService<DespachadorCredito>().LiberarAsync(empresa, ct);

        await liberar.Should().ThrowAsync<InvalidOperationException>();
    }
}
