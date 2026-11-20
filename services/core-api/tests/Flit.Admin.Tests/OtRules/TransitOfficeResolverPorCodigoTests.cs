using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Tramites.Domain.Integration;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.OtRules;

/// <summary>
/// ADR-0084 — <c>ResolveEnabledByCodeAsync</c>: mismo criterio que la resolución por nombre (grant de la lista efectiva +
/// catálogo activo) pero por el código del catálogo, que Diagnóstico toma de su réplica cuando el nombre RUNT casa con un
/// solo organismo. Tolera el cero inicial que algunas fuentes pierden (5001000 = 05001000).
/// </summary>
/// <remarks>Uso de ejemplo: <c>await resolver.ResolveEnabledByCodeAsync(tenant, "05001000", ct)</c>.</remarks>
public sealed class TransitOfficeResolverPorCodigoTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OficinaId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001");
    private static readonly Guid OtraId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    [Theory]
    [InlineData("05001000")]
    [InlineData(" 05001000 ")]
    [InlineData("5001000")]
    public async Task ResuelveElOtHabilitadoPorSuCodigo(string codigo)
    {
        ITransitOfficeResolver resolver = new TransitOfficeResolver(Habilitados(OficinaId), Catalogo());

        var match = await resolver.ResolveEnabledByCodeAsync(Tenant, codigo, TestContext.Current.CancellationToken);

        match.Should().NotBeNull();
        match!.Id.Should().Be(OficinaId);
        match.Code.Should().Be("05001000");
    }

    [Fact]
    public async Task UnCodigoDelCatalogoSinGrantDeLaEmpresa_NoSeResuelve()
    {
        ITransitOfficeResolver resolver = new TransitOfficeResolver(Habilitados(OficinaId), Catalogo());

        var match = await resolver.ResolveEnabledByCodeAsync(Tenant, "11001000", TestContext.Current.CancellationToken);

        match.Should().BeNull("el organismo existe en el catálogo pero no está en la lista efectiva de la empresa");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("99999999")]
    public async Task VacioOInexistente_NoSeResuelve(string codigo)
    {
        ITransitOfficeResolver resolver = new TransitOfficeResolver(Habilitados(OficinaId, OtraId), Catalogo());

        var match = await resolver.ResolveEnabledByCodeAsync(Tenant, codigo, TestContext.Current.CancellationToken);

        match.Should().BeNull();
    }

    private static IEffectiveTransitOfficeListResolver Habilitados(params Guid[] ids)
    {
        var effective = Substitute.For<IEffectiveTransitOfficeListResolver>();
        effective.ListEffectiveOfficeIdsAsync(Tenant, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<Guid>>(ids));
        return effective;
    }

    private static ITransitOfficeCatalog Catalogo()
    {
        var catalog = Substitute.For<ITransitOfficeCatalog>();
        catalog.GetById(OficinaId).Returns(new TransitOfficeEntry(OficinaId, "05001000", "Organismo de prueba", "05", "05001"));
        catalog.GetById(OtraId).Returns(new TransitOfficeEntry(OtraId, "11001000", "Otro organismo de prueba", "11", "11001"));
        return catalog;
    }
}
