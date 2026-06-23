using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

public sealed class DeleteRoleHandlerTests
{
    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();
    private readonly DeleteRoleHandler _handler;

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();

    private static RoleDetail MakeRole(bool isSystem = false) =>
        new(RoleId, TenantId, "ADMIN", "Administrador", null, isSystem, true, []);

    public DeleteRoleHandlerTests()
    {
        _handler = new DeleteRoleHandler(_repo);
    }

    // AC4 — rol existente sin usuarios activos y no system → soft delete exitoso
    [Fact]
    public async Task HandleAsync_NoActiveUsers_SoftDeletes()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(MakeRole());
        _repo.HasActiveUsersAsync(RoleId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler.Invoking(h => h.HandleAsync(RoleId, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteAsync(RoleId, Arg.Any<CancellationToken>());
    }

    // AC3 — rol con IsSystem=true → RoleSystemLockedException, sin soft delete
    [Fact]
    public async Task HandleAsync_IsSystem_ThrowsRoleSystemLocked()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(MakeRole(isSystem: true));

        await _handler
            .Invoking(h => h.HandleAsync(RoleId, CancellationToken.None))
            .Should().ThrowAsync<RoleSystemLockedException>();

        await _repo.DidNotReceiveWithAnyArgs().HasActiveUsersAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // AC5 — rol con usuarios activos → RoleHasActiveUsersException, sin soft delete
    [Fact]
    public async Task HandleAsync_HasActiveUsers_ThrowsRoleHasActiveUsers()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(MakeRole());
        _repo.HasActiveUsersAsync(RoleId, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(RoleId, CancellationToken.None))
            .Should().ThrowAsync<RoleHasActiveUsersException>();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // AC — rol no encontrado → RoleNotFoundException
    [Fact]
    public async Task HandleAsync_NotFound_ThrowsRoleNotFound()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((RoleDetail?)null);

        await _handler
            .Invoking(h => h.HandleAsync(RoleId, CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().HasActiveUsersAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
