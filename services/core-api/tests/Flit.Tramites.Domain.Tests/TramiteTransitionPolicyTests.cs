using System;
using System.Linq;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Estados;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #12596 (Feature #12595, ADR-0059) — política de transición con contexto por encima de la máquina
/// estructural. Un caso por AC más el barrido: toda arista que la máquina no tiene sigue siendo
/// <c>transicion_no_permitida</c> sea cual sea el contexto.
/// </summary>
public sealed class TramiteTransitionPolicyTests
{
    private static TransitionContext Ctx(
        TramiteActor actor,
        bool pidePlaca = true,
        bool tienePlaca = false,
        bool subsanacion = false) =>
        new(pidePlaca, tienePlaca, actor, subsanacion);

    // ---- AC1 — radicar sin placa un tipo que pide placa entra por preasignacion ----

    [Theory]
    [InlineData(TramiteActor.Sistema)]
    [InlineData(TramiteActor.Gestor)]
    public void Ac1_PreparadoAPreasignacion_TipoPidePlacaSinPlaca_EsValida(TramiteActor actor)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Preasignacion, Ctx(actor));

        verdict.Allowed.Should().BeTrue();
        verdict.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void Ac1_PreparadoAPreasignacion_ConPlaca_SeRechazaPorIncoherente()
    {
        // Con placa el submit debe ir a entregado; la política no deja "esconder" la placa en la cola.
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Preasignacion, Ctx(TramiteActor.Gestor, tienePlaca: true));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionPlacaIncoherente);
    }

    [Theory]
    [InlineData(TramiteActor.Ot)]
    [InlineData(TramiteActor.Quipux)]
    public void Ac1_PreparadoAPreasignacion_ActorNoGestor_SeRechaza(TramiteActor actor)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Preasignacion, Ctx(actor));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionSoloGestor);
    }

    // ---- AC2 — sin requiresPlateRequest no existe la ruta de placa ----

    [Theory]
    [InlineData("preparado", "preasignacion", TramiteActor.Gestor)]
    [InlineData("rechazado", "preasignacion", TramiteActor.Gestor)]
    [InlineData("preasignacion", "asignado", TramiteActor.Ot)]
    [InlineData("asignado", "preasignacion", TramiteActor.Ot)]
    public void Ac2_TipoNoPidePlaca_TodaAristaHaciaRutaDePlaca_SeRechaza(string from, string to, TramiteActor actor)
    {
        // Contexto por lo demás "perfecto" (actor correcto, placa coherente, subsanación activa):
        // el único motivo de rechazo debe ser el tipo.
        var ctx = Ctx(actor, pidePlaca: false, tienePlaca: to == TramiteEstado.Asignado, subsanacion: true);

        var verdict = TramiteTransitionPolicy.Evaluate(from, to, ctx);

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionRequierePlaca);
    }

    [Fact]
    public void Ac2_TipoNoPidePlaca_PreparadoAEntregado_SigueSiendoValida()
    {
        // Traspasos y demás tipos: la Ruta Corta de siempre, sin placa y sin tocar la cola.
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Entregado, Ctx(TramiteActor.Gestor, pidePlaca: false));

        verdict.Allowed.Should().BeTrue();
    }

    // ---- AC3 — la cola de placa la mueve el OT ----

    [Theory]
    [InlineData("asignado", true)]
    [InlineData("rechazado", false)]
    public void Ac3_DesdePreasignacion_ActorOt_AsignarYRechazar_SonValidas(string to, bool tienePlaca)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preasignacion, to, Ctx(TramiteActor.Ot, tienePlaca: tienePlaca));

        verdict.Allowed.Should().BeTrue(to);
    }

    [Theory]
    [InlineData("asignado")]
    [InlineData("rechazado")]
    public void Ac3_DesdePreasignacion_ActorGestor_SeRechazaSoloOt(string to)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preasignacion, to, Ctx(TramiteActor.Gestor, tienePlaca: true));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionSoloOt);
    }

    [Fact]
    public void Ac3_PreasignacionAAsignado_SinPlaca_SeRechazaPorIncoherente()
    {
        // 'asignado' significa "tiene placa": el OT no puede avanzar la cola sin escribirla.
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preasignacion, TramiteEstado.Asignado, Ctx(TramiteActor.Ot, tienePlaca: false));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionPlacaIncoherente);
    }

    // ---- AC4 — desde asignado: el gestor envía al OT, el OT libera; nadie decide ----

    [Fact]
    public void Ac4_AsignadoAEntregado_ActorGestor_EsValida()
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Asignado, TramiteEstado.Entregado, Ctx(TramiteActor.Gestor, tienePlaca: true));

        verdict.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Ac4_AsignadoAEntregado_ActorOt_SeRechazaSoloGestor()
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Asignado, TramiteEstado.Entregado, Ctx(TramiteActor.Ot, tienePlaca: true));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionSoloGestor);
    }

    [Fact]
    public void Ac4_AsignadoAPreasignacion_ActorOt_LiberarPlaca_EsValida()
    {
        // Liberar NO borra la placa de field_values (HU #12077): la coherencia de placa no aplica aquí.
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Asignado, TramiteEstado.Preasignacion, Ctx(TramiteActor.Ot, tienePlaca: true));

        verdict.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Ac4_AsignadoAPreasignacion_ActorGestor_SeRechazaSoloOt()
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Asignado, TramiteEstado.Preasignacion, Ctx(TramiteActor.Gestor, tienePlaca: true));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionSoloOt);
    }

    [Theory]
    [InlineData("aprobado")]
    [InlineData("rechazado")]
    public void Ac4_AsignadoADecision_ActorOt_SeRechaza(string to)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Asignado, to, Ctx(TramiteActor.Ot, tienePlaca: true));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
    }

    // ---- AC5 — desde rechazado no hay atajo: la subsanación es la ruta ----

    [Theory]
    [InlineData("preasignacion", false)]
    [InlineData("entregado", true)]
    public void Ac5_RechazadoDirecto_SinSubsanacionActiva_SeRechaza(string to, bool tienePlaca)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Rechazado, to, Ctx(TramiteActor.Gestor, tienePlaca: tienePlaca, subsanacion: false));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionNoPermitida);
    }

    [Theory]
    [InlineData(false, "preasignacion")]
    [InlineData(true, "entregado")]
    public void Ac5_RechazadoConSubsanacionActiva_ReRadica_YLaPlacaDecideElDestino(bool tienePlaca, string destinoEsperado)
    {
        // HU #12597 AC7 — al radicar de nuevo, sin placa → preasignacion; con placa → entregado.
        var ctx = Ctx(TramiteActor.Gestor, tienePlaca: tienePlaca, subsanacion: true);

        TramiteTransitionPolicy.Evaluate(TramiteEstado.Rechazado, destinoEsperado, ctx).Allowed.Should().BeTrue();

        var destinoContrario = destinoEsperado == TramiteEstado.Entregado
            ? TramiteEstado.Preasignacion
            : TramiteEstado.Entregado;
        var contrario = TramiteTransitionPolicy.Evaluate(TramiteEstado.Rechazado, destinoContrario, ctx);
        contrario.ErrorCode.Should().BeOneOf(
            TramiteEstadoErrores.TransicionPlacaIncoherente,
            TramiteEstadoErrores.TransicionRequierePreasignacion);
    }

    [Theory]
    [InlineData("borrador")]
    [InlineData("anulado")]
    public void Ac5_RechazadoASubsanarOAnular_NoDependeDelContexto(string to)
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Rechazado, to, Ctx(TramiteActor.Gestor, pidePlaca: false));

        verdict.Allowed.Should().BeTrue();
    }

    // ---- AC6 — Quipux entrega en un salto aunque el tipo pida placa (ADR-0051) ----

    [Fact]
    public void Ac6_Quipux_PreparadoAEntregado_TipoPidePlacaSinPlaca_EsValida()
    {
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Entregado, Ctx(TramiteActor.Quipux, pidePlaca: true, tienePlaca: false));

        verdict.Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(TramiteActor.Gestor)]
    [InlineData(TramiteActor.Sistema)]
    public void Ac6_NoQuipux_PreparadoAEntregado_TipoPidePlacaSinPlaca_SeRechazaRequierePreasignacion(TramiteActor actor)
    {
        // La Ruta Larga no puede saltarse la cola de placa: es justo lo que el trigger DDL decidía
        // «a espaldas» del lifecycle y ahora decide la política.
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Entregado, Ctx(actor, pidePlaca: true, tienePlaca: false));

        verdict.ErrorCode.Should().Be(TramiteEstadoErrores.TransicionRequierePreasignacion);
    }

    [Fact]
    public void Ac6_Gestor_PreparadoAEntregado_TipoPidePlacaConPlaca_RutaCorta_EsValida()
    {
        // Epic #12550 — placa RUNT o digitada: entregado directo, sin pasar por asignado.
        var verdict = TramiteTransitionPolicy.Evaluate(
            TramiteEstado.Preparado, TramiteEstado.Entregado, Ctx(TramiteActor.Gestor, pidePlaca: true, tienePlaca: true));

        verdict.Allowed.Should().BeTrue();
    }

    // ---- Barrido: la política nunca abre una arista que la máquina no tiene ----

    [Fact]
    public void AristasInexistentes_SeRechazan_SeaCualSeaElContexto()
    {
        var contextos = Enum.GetValues<TramiteActor>()
            .SelectMany(actor => new[] { false, true }
                .SelectMany(pide => new[] { false, true }
                    .SelectMany(tiene => new[] { false, true }
                        .Select(sub => new TransitionContext(pide, tiene, actor, sub)))))
            .ToArray();

        foreach (var from in TramiteEstado.Todos)
        {
            foreach (var to in TramiteEstado.Todos.Where(t => !TramiteStateMachine.IsValidTransition(from, t)))
            {
                foreach (var ctx in contextos)
                {
                    TramiteTransitionPolicy.Evaluate(from, to, ctx).ErrorCode
                        .Should().Be(TramiteEstadoErrores.TransicionNoPermitida, $"{from} → {to} con {ctx}");
                }
            }
        }
    }

    [Fact]
    public void AristasPreviasAAdr0059_NoMiranElActor()
    {
        // Las autorizaciones de aprobar/rechazar/revocar viven en los endpoints, no aquí.
        foreach (var actor in Enum.GetValues<TramiteActor>())
        {
            TramiteTransitionPolicy.Evaluate(TramiteEstado.Entregado, TramiteEstado.Aprobado, Ctx(actor, tienePlaca: true)).Allowed.Should().BeTrue();
            TramiteTransitionPolicy.Evaluate(TramiteEstado.Entregado, TramiteEstado.Rechazado, Ctx(actor)).Allowed.Should().BeTrue();
            TramiteTransitionPolicy.Evaluate(TramiteEstado.Aprobado, TramiteEstado.Revocado, Ctx(actor)).Allowed.Should().BeTrue();
            TramiteTransitionPolicy.Evaluate(TramiteEstado.Borrador, TramiteEstado.Preparado, Ctx(actor)).Allowed.Should().BeTrue();
        }
    }

    [Fact]
    public void ContextoNulo_Lanza()
    {
        var act = () => TramiteTransitionPolicy.Evaluate(TramiteEstado.Preparado, TramiteEstado.Entregado, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
