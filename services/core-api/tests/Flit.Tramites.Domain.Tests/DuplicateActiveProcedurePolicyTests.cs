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
        DuplicateActiveProcedurePolicy.FindActiveDuplicate([]).Should().BeNull();
    }

    [Theory]
    [InlineData(TramiteEstado.Borrador)]
    [InlineData(TramiteEstado.Preparado)]
    [InlineData(TramiteEstado.Entregado)]
    // HU #10870 — subsanación sigue "en proceso": el trámite se está corrigiendo para re-radicarse,
    // así que NO libera la llave de duplicidad (VIN/placa) mientras dura la corrección.
    [InlineData(TramiteEstado.Subsanacion)]
    public void FindActiveDuplicate_EstadoEnProceso_DevuelveId(string estado)
    {
        var id = Guid.NewGuid();
        var existentes = new List<(Guid Id, string Estado)> { (id, estado) };

        DuplicateActiveProcedurePolicy.FindActiveDuplicate(existentes).Should().Be(id);
    }

    [Theory]
    [InlineData(TramiteEstado.Aprobado)]
    [InlineData(TramiteEstado.Rechazado)]
    [InlineData(TramiteEstado.Anulado)]
    public void FindActiveDuplicate_EstadoFinal_Null_LiberaLaLlave(string estado)
    {
        // AC4: los estados finales (aprobado/rechazado/anulado) NO cuentan como "en proceso".
        var existentes = new List<(Guid Id, string Estado)> { (Guid.NewGuid(), estado) };

        DuplicateActiveProcedurePolicy.FindActiveDuplicate(existentes).Should().BeNull();
    }

    [Fact]
    public void FindActiveDuplicate_MezclaFinalesYEnProceso_DevuelveElEnProceso()
    {
        var idFinal = Guid.NewGuid();
        var idEnProceso = Guid.NewGuid();
        var existentes = new List<(Guid Id, string Estado)>
        {
            (idFinal, TramiteEstado.Rechazado),
            (idEnProceso, TramiteEstado.Entregado),
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
