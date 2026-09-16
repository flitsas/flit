using System;
using System.Linq;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class StateMachineTests
{
    // ---- Máquina de estados de negocio N 03 (RF01–RF02, RF04 · ADR-0022) ----

    /// <summary>Únicas aristas de la máquina ESTRUCTURAL (RF02 + ruta de placa ADR-0059); TODO el resto
    /// del producto 9×9 es inválido. Las aristas de placa y la re-radicación exigen además contexto
    /// (ver <see cref="TramiteTransitionPolicyTests"/>).</summary>
    private static readonly (string From, string To)[] TransicionesValidas =
    [
        (TramiteEstado.Borrador, TramiteEstado.Anulado),
        (TramiteEstado.Borrador, TramiteEstado.Preparado),
        (TramiteEstado.Preparado, TramiteEstado.Entregado),
        // ADR-0059 — Ruta Larga: radicar sin placa un tipo que la pide.
        (TramiteEstado.Preparado, TramiteEstado.Preasignacion),
        (TramiteEstado.Preasignacion, TramiteEstado.Asignado),
        (TramiteEstado.Preasignacion, TramiteEstado.Rechazado),
        (TramiteEstado.Asignado, TramiteEstado.Entregado),
        (TramiteEstado.Asignado, TramiteEstado.Preasignacion),
        (TramiteEstado.Entregado, TramiteEstado.Aprobado),
        (TramiteEstado.Entregado, TramiteEstado.Rechazado),
        (TramiteEstado.Rechazado, TramiteEstado.Borrador),
        (TramiteEstado.Rechazado, TramiteEstado.Anulado),
        // Re-radicar tras activar flag de subsanación (la política exige el flag); el destino lo
        // decide la placa.
        (TramiteEstado.Rechazado, TramiteEstado.Entregado),
        (TramiteEstado.Rechazado, TramiteEstado.Preasignacion),
        // HU #12166 (Feature #12156) — única salida de 'aprobado': el OT revoca su propia aprobación.
        (TramiteEstado.Aprobado, TramiteEstado.Revocado),
    ];

    [Theory]
    [InlineData("borrador", "anulado")]
    [InlineData("borrador", "preparado")]
    [InlineData("preparado", "entregado")]
    [InlineData("preparado", "preasignacion")]
    [InlineData("preasignacion", "asignado")]
    [InlineData("preasignacion", "rechazado")]
    [InlineData("asignado", "entregado")]
    [InlineData("asignado", "preasignacion")]
    [InlineData("entregado", "aprobado")]
    [InlineData("entregado", "rechazado")]
    [InlineData("rechazado", "borrador")]
    [InlineData("rechazado", "anulado")]
    [InlineData("rechazado", "entregado")]
    [InlineData("rechazado", "preasignacion")]
    [InlineData("aprobado", "revocado")]
    public void Negocio_TransicionesValidasRf02(string from, string to)
    {
        TramiteStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    [Fact]
    public void Negocio_ProductoCartesianoCompleto_SoloRf02EsValido()
    {
        // Cobertura exhaustiva 9×9: cada par (from, to) es válido si y solo si está en la lista.
        foreach (var from in TramiteEstado.Todos)
        {
            foreach (var to in TramiteEstado.Todos)
            {
                var esperado = TransicionesValidas.Contains((from, to));
                TramiteStateMachine.IsValidTransition(from, to)
                    .Should().Be(esperado, $"transición {from} → {to}");
            }
        }
    }

    [Fact]
    public void Negocio_AprobadoYAnuladoSonTerminales()
    {
        // RF04 — finales por EDICIÓN DE DATOS y ciclo de vida NORMAL del trámite: el único movimiento
        // posterior a 'aprobado' es la revocación del OT (HU #12166), que no reabre el trámite ni sus
        // datos, así que 'aprobado' se sigue tratando como terminal a efectos de negocio (EsFinal).
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Aprobado)
            .Should().BeEquivalentTo([TramiteEstado.Revocado]);
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Anulado).Should().BeEmpty();
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Revocado).Should().BeEmpty();
        TramiteEstado.EsFinal(TramiteEstado.Aprobado).Should().BeTrue();
        TramiteEstado.EsFinal(TramiteEstado.Anulado).Should().BeTrue();
        TramiteEstado.EsFinal(TramiteEstado.Revocado).Should().BeTrue();
        TramiteEstado.EsFinal(TramiteEstado.Borrador).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Preparado).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Entregado).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Rechazado).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Preasignacion).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Asignado).Should().BeFalse();
    }

    [Fact]
    public void Negocio_RutaDePlaca_NoDecideNiSeDecideFueraDeEntregado()
    {
        // ADR-0059 — la decisión del OT (aprobar) es exclusiva de 'entregado': ni preasignacion ni
        // asignado llegan a aprobado; asignado tampoco se rechaza (primero se libera la placa).
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Preasignacion)
            .Should().BeEquivalentTo([TramiteEstado.Asignado, TramiteEstado.Rechazado]);
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Asignado)
            .Should().BeEquivalentTo([TramiteEstado.Entregado, TramiteEstado.Preasignacion]);
        TramiteStateMachine.IsValidTransition(TramiteEstado.Entregado, TramiteEstado.Asignado).Should().BeFalse();
        TramiteStateMachine.IsValidTransition(TramiteEstado.Entregado, TramiteEstado.Preasignacion).Should().BeFalse();
    }

    [Fact]
    public void Negocio_RutaDePlaca_EsRecibidaPorElOrganismo_EnProceso_YOcupaPlaca()
    {
        // HU #12596 AC7 — el OT ve la cola de placa; los dos estados bloquean duplicidad (CF-01) y
        // retienen la placa.
        foreach (var estado in TramiteEstado.EstadosDeRutaDePlaca)
        {
            TramiteEstado.EsEstadoDeRutaDePlaca(estado).Should().BeTrue(estado);
            TramiteEstado.EstaEnManosDelOrganismo(estado).Should().BeTrue(estado);
            TramiteEstado.EstaEnProceso(estado).Should().BeTrue(estado);
            TramiteEstado.OcupaPlaca(estado).Should().BeTrue(estado);
            TramiteEstado.PermiteEdicionDatos(estado).Should().BeFalse(estado);
        }

        TramiteEstado.EsEstadoDeRutaDePlaca(TramiteEstado.Entregado).Should().BeFalse();
        TramiteEstado.EsEstadoDeRutaDePlaca("preasignado").Should().BeFalse("el literal legado del sub-estado no es un estado");
        TramiteEstado.EsEstadoDeRutaDePlaca(null).Should().BeFalse();
    }

    [Fact]
    public void Negocio_EstadosDesconocidosONulosNoTransicionan()
    {
        TramiteStateMachine.IsValidTransition("draft", TramiteEstado.Preparado).Should().BeFalse();
        TramiteStateMachine.IsValidTransition(TramiteEstado.Borrador, "submitted").Should().BeFalse();
        TramiteStateMachine.IsValidTransition(null, TramiteEstado.Preparado).Should().BeFalse();
        TramiteStateMachine.IsValidTransition(TramiteEstado.Borrador, null).Should().BeFalse();
        TramiteStateMachine.TransitionsFrom("inexistente").Should().BeEmpty();
        TramiteStateMachine.TransitionsFrom(null).Should().BeEmpty();
    }

    [Fact]
    public void Negocio_TodoDestinoEsEstadoValido()
    {
        foreach (var from in TramiteEstado.Todos)
        {
            foreach (var to in TramiteStateMachine.TransitionsFrom(from))
                TramiteEstado.EsValido(to).Should().BeTrue();
        }
    }

    [Fact]
    public void Negocio_EsValidoReconoceLosEstadosDelCicloDeVida()
    {
        // 9 estados de negocio (HU #12165 agrega 'revocado'; ADR-0059 agrega 'preasignacion' y
        // 'asignado'): la subsanación es flag sobre rechazado, no un estado propio.
        TramiteEstado.Todos.Should().HaveCount(9);
        foreach (var estado in TramiteEstado.Todos)
            TramiteEstado.EsValido(estado).Should().BeTrue();
        TramiteEstado.EsValido("subsanacion").Should().BeFalse();
        TramiteEstado.EsValido("preasignado").Should().BeFalse(); // literal del sub-estado legado, no un estado
        TramiteEstado.EsValido("terminado").Should().BeFalse();
        TramiteEstado.EsValido("draft").Should().BeFalse();
        TramiteEstado.EsValido("Borrador").Should().BeFalse(); // case-sensitive: se persiste en minúscula
        TramiteEstado.EsValido(null).Should().BeFalse();
        TramiteEstado.EsValido("").Should().BeFalse();
    }

    // ---- Workflow STT (se conserva: independiente del ciclo de vida N 03) ----

    [Fact]
    public void Stt_TransicionesValidasEInvalidas()
    {
        SttWorkflow.IsValidTransition(TramiteEstadoStt.Radicado, TramiteEstadoStt.EnValidacion).Should().BeTrue();
        SttWorkflow.IsValidTransition(TramiteEstadoStt.Aprobado, TramiteEstadoStt.Entregado).Should().BeTrue();
        SttWorkflow.IsValidTransition(TramiteEstadoStt.Entregado, TramiteEstadoStt.Radicado).Should().BeFalse();
        SttWorkflow.IsValidTransition(TramiteEstadoStt.Anulado, TramiteEstadoStt.Radicado).Should().BeFalse();
    }

    [Fact]
    public void Stt_ExpedienteEditable()
    {
        SttWorkflow.ExpedienteEditable(TramiteEstadoStt.Radicado).Should().BeTrue();
        SttWorkflow.ExpedienteEditable(TramiteEstadoStt.Subsanacion).Should().BeTrue();
        SttWorkflow.ExpedienteEditable(TramiteEstadoStt.EnTramite).Should().BeFalse();
        SttWorkflow.GestionCerrada(TramiteEstadoStt.EnTramite).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 2026, "TD-2026-00001")]
    [InlineData(42, 2026, "TD-2026-00042")]
    [InlineData(99999, 2025, "TD-2025-99999")]
    public void Stt_FormatRadicado(int seq, int year, string expected)
    {
        SttWorkflow.FormatRadicado(seq, year).Should().Be(expected);
    }
}
