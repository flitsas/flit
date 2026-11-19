using Flit.Api.Grpc;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// El nombre del organismo del RUNT que llega de ICT se resuelve a un OT habilitado del tenant. Si el nombre SÍ es un
/// OT del catálogo pero no está activo, no tiene grant para el tenant o no está operativo, el borrador NO se crea y se
/// devuelve <c>ot_runt_no_habilitado</c>; si el nombre no casa con ningún OT, se mantiene el comportamiento previo
/// (sin OT, lo asigna el gestor).
/// </summary>
public sealed class IctOtRuntNoHabilitadoTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly ResolvedTransitOffice Sabaneta = new(Guid.NewGuid(), "05631000", "Sabaneta", "05631", "Sabaneta");

    private readonly ITransitOfficeResolver _resolver = Substitute.For<ITransitOfficeResolver>();
    private readonly IOtOperabilityGate _gate = Substitute.For<IOtOperabilityGate>();

    private Task<(ResolvedTransitOffice? Office, bool NoHabilitado)> Resolver(string nombre) =>
        IctOrchestrationService.ResolverOtPorNombreRuntAsync(
            _resolver, _gate, Tenant, nombre, TestContext.Current.CancellationToken);

    [Fact]
    public void ElCodigoDeErrorEsElContratoConCoreIct() =>
        IctOrchestrationService.ErrorOtRuntNoHabilitado.Should().Be("ot_runt_no_habilitado");

    [Fact]
    public async Task OtHabilitadoYOperativo_SeAsigna()
    {
        _resolver.ResolveEnabledByNameAsync(Tenant, "Sabaneta", Arg.Any<CancellationToken>()).Returns(Sabaneta);
        _gate.IsOperableAsync(Sabaneta.Id, Arg.Any<CancellationToken>()).Returns(true);

        var (office, noHabilitado) = await Resolver(" Sabaneta ");

        office.Should().Be(Sabaneta);
        noHabilitado.Should().BeFalse();
    }

    [Fact]
    public async Task OtHabilitadoPeroNoOperativo_EsError()
    {
        _resolver.ResolveEnabledByNameAsync(Tenant, "Sabaneta", Arg.Any<CancellationToken>()).Returns(Sabaneta);
        _gate.IsOperableAsync(Sabaneta.Id, Arg.Any<CancellationToken>()).Returns(false);

        var (office, noHabilitado) = await Resolver("Sabaneta");

        office.Should().BeNull();
        noHabilitado.Should().BeTrue();
    }

    [Fact]
    public async Task OtDelCatalogoSinGrantOInactivo_EsError()
    {
        // ResolveEnabledByName devuelve null (sin grant del tenant o inactivo) pero el nombre SÍ está en el catálogo.
        _resolver.ResolveEnabledByNameAsync(Tenant, "Sabaneta", Arg.Any<CancellationToken>())
            .Returns((ResolvedTransitOffice?)null);
        _resolver.ExistsInCatalogByNameAsync("Sabaneta", Arg.Any<CancellationToken>()).Returns(true);

        var (office, noHabilitado) = await Resolver("Sabaneta");

        office.Should().BeNull();
        noHabilitado.Should().BeTrue();
    }

    [Fact]
    public async Task NombreQueNoCasaConNingunOt_MantieneElComportamientoPrevio_SinError()
    {
        _resolver.ResolveEnabledByNameAsync(Tenant, "Municipio inexistente", Arg.Any<CancellationToken>())
            .Returns((ResolvedTransitOffice?)null);
        _resolver.ExistsInCatalogByNameAsync("Municipio inexistente", Arg.Any<CancellationToken>()).Returns(false);

        var (office, noHabilitado) = await Resolver("Municipio inexistente");

        office.Should().BeNull();
        noHabilitado.Should().BeFalse("el gestor asigna el OT; no se inventa uno ni se falla");
        await _gate.DidNotReceive().IsOperableAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
