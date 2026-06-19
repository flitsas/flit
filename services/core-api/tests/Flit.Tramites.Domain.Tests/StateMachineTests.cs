using System;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Services;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class StateMachineTests
{
    [Fact]
    public void Interno_TransicionesValidasConocidas()
    {
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.Borrador, TramiteEstadoInterno.EnviadoTransito).Should().BeTrue();
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.RecibidoTransito, TramiteEstadoInterno.PlacaPreasignada).Should().BeTrue();
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.PlacaPreasignada, TramiteEstadoInterno.SolicitudSoat).Should().BeTrue();
    }

    [Fact]
    public void Interno_TransicionesInvalidas()
    {
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.Borrador, TramiteEstadoInterno.Completado).Should().BeFalse();
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.EnviadoTransito, TramiteEstadoInterno.Borrador).Should().BeFalse();
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.EnviadoTransito, TramiteEstadoInterno.Rechazado).Should().BeTrue();
        TramiteStateMachine.IsValidTransition(TramiteEstadoInterno.Completado, TramiteEstadoInterno.Borrador).Should().BeFalse();
    }

    [Fact]
    public void Interno_TodoDestinoEsEstadoValido()
    {
        foreach (var from in Enum.GetValues<TramiteEstadoInterno>())
        {
            foreach (var to in TramiteStateMachine.TransitionsFrom(from))
                Enum.IsDefined(to).Should().BeTrue();
        }
    }

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

    [Fact]
    public void StatusMapper_MapeaAEstadosDePersistencia()
    {
        TramiteStatusMapper.ToPersistenceStatus(TramiteEstadoInterno.Borrador)
            .Should().Be(Domain.Enums.ProcedureInstanceStatus.Draft);
        TramiteStatusMapper.ToPersistenceStatus(TramiteEstadoInterno.Radicado)
            .Should().Be(Domain.Enums.ProcedureInstanceStatus.Submitted);
        TramiteStatusMapper.ToPersistenceStatus(TramiteEstadoInterno.Documentos)
            .Should().Be(Domain.Enums.ProcedureInstanceStatus.InReview);
        TramiteStatusMapper.ToPersistenceStatus(TramiteEstadoInterno.Completado)
            .Should().Be(Domain.Enums.ProcedureInstanceStatus.Completed);
        TramiteStatusMapper.ToPersistenceStatus(TramiteEstadoInterno.Rechazado)
            .Should().Be(Domain.Enums.ProcedureInstanceStatus.Cancelled);
    }
}
