using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.Ejecucion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Liquidaciones;

/// <summary>
/// HU #14694 (M12; F3-CF5, F3-CF12, plan §3.3) — la terminación del lote tiene una sola fuente: tras cada cambio de ítem,
/// <c>EvaluadorTerminacion</c> recalcula los contadores con <c>ContadoresLote</c> (M10) y, si el lote entró en un estado terminal,
/// dispara el aviso. Todos los ítems en estado final y todos <c>EXITOSO</c> ⇒ <c>COMPLETADO</c>; con alguno que no lo es ⇒
/// <c>COMPLETADO_CON_NOVEDADES</c>; <c>INCIERTO_REQUIERE_DECISION</c> cuenta como final (DA-6b). Con un ítem pendiente el lote sigue
/// <c>EN_PROCESO</c> y no sale aviso.
/// </summary>
/// <example>
/// <code>
/// await EntornoResultados.AplicarAsync(api, empresa, item, EntornoResultados.Consulta("c-1"), ct);
/// </code>
/// </example>
public sealed class EvaluadorTerminacionTests
{
    [Fact]
    public async Task Todos_exitosos_deja_el_lote_COMPLETADO_con_terminadaEn_y_un_aviso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, items) = await SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Enviado);

        await R.AplicarAsync(api, empresa, items[0], R.Consulta("c-0"), ct);
        (await R.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.EnProceso, "falta un ítem");
        (await M.NotificacionesAsync(api, empresa, ct)).Should().BeEmpty();
        await R.AplicarAsync(api, empresa, items[1], R.Consulta("c-1", resultStatus: "paz_y_salvo", totalToPay: "0"), ct);

        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.Completado);
        cabecera.TerminadaEn.Should().NotBeNull();
        cabecera.AvisoEnviadoEn.Should().NotBeNull();
        cabecera.CExitosas.Should().Be(2);
        cabecera.CConDeuda.Should().Be(1);
        cabecera.CPazYSalvo.Should().Be(1);
        cabecera.CPendientes.Should().Be(0);
        (await M.NotificacionesAsync(api, empresa, ct)).Should().ContainSingle().Which.LiquidacionId.Should().Be(lote);
    }

    [Fact]
    public async Task Con_un_item_en_error_termina_COMPLETADO_CON_NOVEDADES()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, items) = await SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.DatosIncompletos);

        await R.AplicarAsync(api, empresa, items[0], R.Consulta("c-0"), ct);

        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CompletadoConNovedades);
        cabecera.CDatosIncompletos.Should().Be(1);
        (await M.NotificacionesAsync(api, empresa, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task INCIERTO_REQUIERE_DECISION_cuenta_como_final_para_terminar_el_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, items) = await SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.InciertoRequiereDecision);

        await R.AplicarAsync(api, empresa, items[0], R.Consulta("c-0"), ct);

        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CompletadoConNovedades, "DA-6b: requiere decisión es final para el cierre");
        cabecera.CIncierto.Should().Be(1);
        (await M.NotificacionesAsync(api, empresa, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Con_un_item_pendiente_el_lote_sigue_EN_PROCESO_y_no_avisa()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await M.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, lote, items) = await SembrarMasivaAsync(api, ct, EstadoItem.Enviado, EstadoItem.Incierto);

        await R.AplicarAsync(api, empresa, items[0], R.Consulta("c-0"), ct);

        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.EnProceso, "INCIERTO todavía no es final");
        cabecera.TerminadaEn.Should().BeNull();
        cabecera.AvisoEnviadoEn.Should().BeNull();
        (await M.NotificacionesAsync(api, empresa, ct)).Should().BeEmpty();
        (await M.CorreosAsync(api, ct)).Should().BeEmpty();
    }

    /// <summary>Compañía lista con correos de aviso y un lote MASIVA en proceso con un ítem por estado (con consulta <c>c-{n}</c>).</summary>
    internal static async Task<(Guid Empresa, Guid Lote, IReadOnlyList<Guid> Items)> SembrarMasivaAsync(
        ApiDePrueba api, CancellationToken ct, params EstadoItem[] estados)
    {
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await M.SembrarConfiguracionAsync(api, empresa, ct, correos: ["aviso@empresa.com"]);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [.. estados.Select((_, i) => $"AAA{i:000}")], ct, tipo: TipoLiquidacion.Masiva);
        await using var db = api.Contexto(empresa);
        await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.EnProceso), ct);
        for (var i = 0; i < estados.Length; i++)
        {
            var id = items[i];
            var estado = estados[i];
            var consulta = $"c-{i}";
            await db.Set<LiquidacionItem>().Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, estado).SetProperty(x => x.ConsultaApiId, consulta), ct);
        }

        return (empresa, lote, items);
    }
}
