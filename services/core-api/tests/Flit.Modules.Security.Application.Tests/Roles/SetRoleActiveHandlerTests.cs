using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

public sealed class SetRoleActiveHandlerTests
{
    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();
    private readonly SetRoleActiveHandler _handler;

    private static readonly Guid RoleId = Guid.NewGuid();

    private static RoleDetail MakeRole() =>
        new(RoleId, "TRANSIT_OFFICE", "ot_admin", "Administrador OT", null, true, true, []);

    public SetRoleActiveHandlerTests()
    {
        _handler = new SetRoleActiveHandler(_repo);
    }

    // HU #10505 — desactiva un rol existente del catálogo global
    [Fact]
    public async Task HandleAsync_ExistingRole_SetsActiveFlag()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(MakeRole());

        await _handler.HandleAsync(RoleId, false, CancellationToken.None);

        await _repo.Received(1).SetActiveAsync(RoleId, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RoleNotFound_ThrowsRoleNotFound_WithoutSettingActive()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((RoleDetail?)null);

        await _handler
            .Invoking(h => h.HandleAsync(RoleId, true, CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().SetActiveAsync(
            Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
