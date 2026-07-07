using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

public sealed class ListRolesHandlerTests
{
    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();
    private readonly ListRolesHandler _handler;

    public ListRolesHandlerTests()
    {
        _handler = new ListRolesHandler(_repo);
    }

    // HU #10505 — lista roles del catálogo global filtrando por targetEntityType (ya no por tenant)
    [Fact]
    public async Task HandleAsync_DelegatesToRepository_FilteringByTargetEntityType()
    {
        var expected = new List<RoleSummary>
        {
            new(Guid.NewGuid(), "AdminCompany", "Administrador de Compañía", null, true, 8, DateTimeOffset.UtcNow),
        };
        _repo.ListByTargetEntityTypeAsync("COMPANY", Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _handler.HandleAsync("COMPANY", CancellationToken.None);

        result.Should().BeEquivalentTo(expected);
        await _repo.Received(1).ListByTargetEntityTypeAsync("COMPANY", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_TransitOffice_DelegatesWithThatTargetEntityType()
    {
        _repo.ListByTargetEntityTypeAsync("TRANSIT_OFFICE", Arg.Any<CancellationToken>())
            .Returns(new List<RoleSummary>());

        await _handler.HandleAsync("TRANSIT_OFFICE", CancellationToken.None);

        await _repo.Received(1).ListByTargetEntityTypeAsync("TRANSIT_OFFICE", Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ListByTargetEntityTypeAsync("COMPANY", Arg.Any<CancellationToken>());
    }
}
