using Flit.Impuestos.Domain;
using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Xunit;
using L = Flit.Impuestos.Domain.Liquidaciones.EstadoLote;
using S = Flit.Impuestos.Domain.Liquidaciones.EstadoItem;

namespace Flit.Impuestos.Tests.Dominio;

/// <summary>
/// HU #14694 (M05) — estado del lote (plan §3.3, S-A: la individual es un lote de un ítem). El lote termina cuando todos sus
/// ítems están en estado final, incluido <c>INCIERTO_REQUIERE_DECISION</c> (DA-6b); el cierre manual solo vale antes de terminar
/// y el aviso sale una sola vez, al entrar en un estado terminal.
/// </summary>
/// <example>
/// <code>
/// CierreLote.Evaluar(EstadoLote.EnProceso, [EstadoItem.Exitoso, EstadoItem.Error]); // CompletadoConNovedades
/// </code>
/// </example>
public sealed class CierreLoteTests
{
    [Fact]
    public void El_primer_item_despachado_pone_el_lote_en_proceso()
    {
        CierreLote.Despachar(L.Creado).Should().Be(L.EnProceso);
        CierreLote.Despachar(L.EnProceso).Should().Be(L.EnProceso, "los siguientes despachos no cambian nada");
    }

    [Theory]
    [InlineData(L.Completado)]
    [InlineData(L.CompletadoConNovedades)]
    [InlineData(L.CerradoManualmente)]
    public void Un_lote_terminado_no_despacha(L estado)
    {
        var act = () => CierreLote.Despachar(estado);

        act.Should().Throw<TransicionInvalida>();
    }

    [Fact]
    public void Todos_exitosos_completa()
    {
        CierreLote.Evaluar(L.EnProceso, [S.Exitoso, S.Exitoso]).Should().Be(L.Completado);
    }

    [Theory]
    [InlineData(S.Error)]
    [InlineData(S.DatosIncompletos)]
    [InlineData(S.DepartamentoNoHabilitado)]
    [InlineData(S.InciertoRequiereDecision)]
    [InlineData(S.SinResultadoLoteCerrado)]
    public void Todos_finales_con_alguno_no_exitoso_completa_con_novedades(S otro)
    {
        CierreLote.Evaluar(L.EnProceso, [S.Exitoso, otro]).Should().Be(L.CompletadoConNovedades);
    }

    [Theory]
    [InlineData(S.Pendiente)]
    [InlineData(S.Enviado)]
    [InlineData(S.Incierto)]
    [InlineData(S.ReintentoProgramado)]
    public void Con_un_item_no_final_sigue_en_proceso(S pendiente)
    {
        CierreLote.Evaluar(L.EnProceso, [S.Exitoso, pendiente]).Should().Be(L.EnProceso);
    }

    [Fact]
    public void Sin_items_o_sin_despachar_no_cambia()
    {
        CierreLote.Evaluar(L.EnProceso, []).Should().Be(L.EnProceso);
        CierreLote.Evaluar(L.Creado, [S.DatosIncompletos]).Should().Be(L.Creado, "el lote pasa a en proceso al despachar, no al evaluar");
    }

    [Theory]
    [InlineData(L.Completado)]
    [InlineData(L.CompletadoConNovedades)]
    [InlineData(L.CerradoManualmente)]
    public void Un_resultado_tardio_no_cambia_un_lote_terminado(L terminado)
    {
        CierreLote.Evaluar(terminado, [S.Exitoso]).Should().Be(terminado, "se recalculan los contadores, no el estado");
    }

    [Theory]
    [InlineData(L.Creado)]
    [InlineData(L.EnProceso)]
    public void Cierre_manual_antes_de_terminar(L desde)
    {
        CierreLote.CerrarManualmente(desde).Should().Be(L.CerradoManualmente);
    }

    [Theory]
    [InlineData(L.Completado)]
    [InlineData(L.CompletadoConNovedades)]
    [InlineData(L.CerradoManualmente)]
    public void Cierre_manual_de_un_lote_terminado_es_YA_TERMINADA(L terminado)
    {
        var act = () => CierreLote.CerrarManualmente(terminado);

        act.Should().Throw<TransicionInvalida>().Which.Codigo.Should().Be(CierreLote.CodigoYaTerminada);
        CierreLote.CodigoYaTerminada.Should().Be("YA_TERMINADA");
    }

    [Theory]
    [InlineData(L.Creado, false)]
    [InlineData(L.EnProceso, false)]
    [InlineData(L.Completado, true)]
    [InlineData(L.CompletadoConNovedades, true)]
    [InlineData(L.CerradoManualmente, true)]
    public void Estados_terminales(L estado, bool terminal)
    {
        CierreLote.EsTerminal(estado).Should().Be(terminal);
    }

    [Fact]
    public void El_aviso_sale_una_sola_vez_al_entrar_en_un_terminal()
    {
        CierreLote.DebeAvisar(L.EnProceso, L.Completado, avisoYaEnviado: false).Should().BeTrue();
        CierreLote.DebeAvisar(L.EnProceso, L.CerradoManualmente, avisoYaEnviado: false).Should().BeTrue();
        CierreLote.DebeAvisar(L.EnProceso, L.Completado, avisoYaEnviado: true).Should().BeFalse("aviso_enviado_en ya tiene fecha");
        CierreLote.DebeAvisar(L.Completado, L.Completado, avisoYaEnviado: false).Should().BeFalse("un resultado tardío no repite el aviso");
        CierreLote.DebeAvisar(L.EnProceso, L.EnProceso, avisoYaEnviado: false).Should().BeFalse();
    }
}
