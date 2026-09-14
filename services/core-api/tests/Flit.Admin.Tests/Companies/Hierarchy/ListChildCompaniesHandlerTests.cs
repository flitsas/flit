using Flit.Admin.Application.Companies.Children.ListChildCompanies;
using Flit.Admin.Domain.Companies;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Hierarchy;

public sealed class ListChildCompaniesHandlerTests
{
    private static readonly Guid Head = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Child = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task HandleAsync_DevuelveHijosDeLaCabeza()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        var expected = new List<CompanyChildListItem>
        {
            new()
            {
                Id = Child,
                Nit = "900111222-3",
                RazonSocial = "Hijo Demo",
                Code = "HIJO1",
                TenantType = "CONCESIONARIO",
                EstadoActivo = true,
                FechaVinculacion = DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
                RowVersion = 1,
            },
        };
        hierarchy.ListChildrenAsync(Head, Arg.Any<CancellationToken>()).Returns(expected);

        var handler = new ListChildCompaniesHandler(hierarchy);
        var result = await handler.HandleAsync(Head);

        result.Should().BeEquivalentTo(expected);
        await hierarchy.Received(1).ListChildrenAsync(Head, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_SinHijos_ListaVacia()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.ListChildrenAsync(Head, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyChildListItem>());

        var handler = new ListChildCompaniesHandler(hierarchy);
        var result = await handler.HandleAsync(Head);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_HijoInexistente_Null()
    {
        var hierarchy = Substitute.For<ICompanyHierarchyRepository>();
        hierarchy.GetChildAsync(Head, Child, Arg.Any<CancellationToken>())
            .Returns((CompanyChildListItem?)null);

        var handler = new ListChildCompaniesHandler(hierarchy);
        var result = await handler.GetAsync(Head, Child);

        result.Should().BeNull();
    }
}
