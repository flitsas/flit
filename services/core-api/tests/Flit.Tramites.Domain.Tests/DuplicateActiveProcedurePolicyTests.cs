using System;
using System.Collections.Generic;
using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class DuplicateActiveProcedurePolicyTests
{
    [Fact]
    public void FindActiveDuplicate_SinFilas_Null()
    {
        DuplicateActiveProcedurePolicy
            .FindActiveDuplicate(Array.Empty<(Guid, string, bool)>())
            .Should().BeNull();
    }

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    [InlineData(TramiteEstado.Subsanacion)]
    public void FindActiveDuplicate_EstadoEnProceso_DevuelveId(string estado)
    {
        var id = Guid.NewGuid();
        var existentes = new List<(Guid Id, string Estado, bool SubsanacionActiva)>
        {
            (id, estado, false),
        };

        DuplicateActiveProcedurePolicy.FindActiveDuplicate(existentes).Should().Be(id);
    }

    [Fact]
    public void FindActiveDuplicate_RechazadoConSubsanacionActiva_DevuelveId()
    {
        var id = Guid.NewGuid();
        var existentes = new List<(Guid Id, string Estado, bool SubsanacionActiva)>
        {
            (id, TramiteEstado.Rechazado, true),
        };

        DuplicateActiveProcedurePolicy.FindActiveDuplicate(existentes).Should().Be(id);
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public void FindActiveDuplicate_EstadoFinal_Null_LiberaLaLlave(string estado)
    {
        var existentes = new List<(Guid Id, string Estado, bool SubsanacionActiva)>
        {
            (Guid.NewGuid(), estado, false),
        };

        DuplicateActiveProcedurePolicy.FindActiveDuplicate(existentes).Should().BeNull();
    }

    [Fact]
    public void FindActiveDuplicate_MezclaFinalesYEnProceso_DevuelveElEnProceso()
    {
        var idFinal = Guid.NewGuid();
        var idEnProceso = Guid.NewGuid();
        var existentes = new List<(Guid Id, string Estado, bool SubsanacionActiva)>
        {
            (idFinal, TramiteEstado.Rechazado, false),
            (idEnProceso, TramiteEstado.Entregado, false),
        };

        DuplicateActiveProcedurePolicy.FindActiveDuplicate(existentes).Should().Be(idEnProceso);
    }

    [Fact]
    public void FindActiveDuplicate_NullList_LanzaArgumentNullException()
    {
        var act = () => DuplicateActiveProcedurePolicy.FindActiveDuplicate(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
