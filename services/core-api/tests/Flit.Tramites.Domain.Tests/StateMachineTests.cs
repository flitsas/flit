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

    /// <summary>Únicas transiciones permitidas por RF02; TODO el resto del producto 6×6 es inválido.
    /// La ruta de placa (Feature #10587 / HU #10785) NO añade estados de trámite: su progreso vive en el
    /// sub-estado interno <see cref="PlateFlowStatus"/> (ver PlateFlowStateMachineTests).</summary>
    private static readonly (string From, string To)[] TransicionesValidas =
    [
        (TramiteEstado.Borrador, TramiteEstado.Anulado),
        (TramiteEstado.Borrador, TramiteEstado.Preparado),
        (TramiteEstado.Preparado, TramiteEstado.Entregado),
        (TramiteEstado.Entregado, TramiteEstado.Aprobado),
        (TramiteEstado.Entregado, TramiteEstado.Rechazado),
        (TramiteEstado.Rechazado, TramiteEstado.Borrador),
        (TramiteEstado.Rechazado, TramiteEstado.Anulado),
        // Re-radicar tras activar flag de subsanación (lifecycle exige el flag).
        (TramiteEstado.Rechazado, TramiteEstado.Entregado),
    ];

    [Theory]
    [InlineData("borrador", "anulado")]
    [InlineData("borrador", "preparado")]
    [InlineData("preparado", "entregado")]
    [InlineData("entregado", "aprobado")]
    [InlineData("entregado", "rechazado")]
    [InlineData("rechazado", "borrador")]
    [InlineData("rechazado", "anulado")]
    [InlineData("rechazado", "entregado")]
    public void Negocio_TransicionesValidasRf02(string from, string to)
    {
        TramiteStateMachine.IsValidTransition(from, to).Should().BeTrue();
    }

    [Fact]
    public void Negocio_ProductoCartesianoCompleto_SoloRf02EsValido()
    {
        // Cobertura exhaustiva 6×6: cada par (from, to) es válido si y solo si está en RF02.
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
        // RF04 — estados finales: sin transiciones posteriores.
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Aprobado).Should().BeEmpty();
        TramiteStateMachine.TransitionsFrom(TramiteEstado.Anulado).Should().BeEmpty();
        TramiteEstado.EsFinal(TramiteEstado.Aprobado).Should().BeTrue();
        TramiteEstado.EsFinal(TramiteEstado.Anulado).Should().BeTrue();
        TramiteEstado.EsFinal(TramiteEstado.Borrador).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Preparado).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Entregado).Should().BeFalse();
        TramiteEstado.EsFinal(TramiteEstado.Rechazado).Should().BeFalse();
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
        // 6 estados de negocio: la subsanación es flag sobre rechazado, no un 7º estado.
        TramiteEstado.Todos.Should().HaveCount(6);
        foreach (var estado in TramiteEstado.Todos)
            TramiteEstado.EsValido(estado).Should().BeTrue();
        TramiteEstado.EsValido("subsanacion").Should().BeFalse();
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
