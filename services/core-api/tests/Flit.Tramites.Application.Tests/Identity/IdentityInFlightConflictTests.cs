using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Identity;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Identity;

public sealed class IdentityInFlightConflictTests
{
    [Fact]
    public void InFlightRaceConflict_ProduceDecisionNoEnviarConMotivoEnVuelo()
    {
        var d = IdentitySendDecisionForTramite.InFlightRaceConflict(IdentitySendOrigen.Tramite);

        d.Kind.Should().Be(IdentitySendDecisionKind.NoEnviar);
        d.Motivo.Should().Be(IdentitySendMotivo.ValidacionEnVuelo);
        d.Origen.Should().Be(IdentitySendOrigen.Tramite);
    }

    [Fact]
    public void IdentityInFlightConflictException_MensajeSinPii()
    {
        var ex = new IdentityInFlightConflictException();
        ex.Message.Should().NotContain("CC");
        ex.Message.Should().NotContain("@");
        ex.Message.Should().Contain("en curso");
    }
}
