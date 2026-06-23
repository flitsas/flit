using Flit.Modules.Security.Application.Permissions;
using Flit.Modules.Security.Domain.Permissions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Permissions;

public sealed class DeletePermissionHandlerTests
{
    private readonly IPermissionRepository _repo = Substitute.For<IPermissionRepository>();
    private readonly DeletePermissionHandler _handler;

    private static readonly Guid PermissionId = Guid.NewGuid();

    public DeletePermissionHandlerTests()
    {
        _handler = new DeletePermissionHandler(_repo);
    }

    // AC6 — permiso existente sin uso en roles → soft delete exitoso
    [Fact]
    public async Task HandleAsync_NotInUse_SoftDeletes()
    {
        _repo.ExistsAsync(PermissionId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.IsInUseByRolesAsync(PermissionId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler.Invoking(h => h.HandleAsync(PermissionId, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteAsync(PermissionId, Arg.Any<CancellationToken>());
    }

    // AC7 — permiso en uso por roles → PermissionInUseException
    [Fact]
    public async Task HandleAsync_InUseByRole_ThrowsPermissionInUse()
    {
        _repo.ExistsAsync(PermissionId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.IsInUseByRolesAsync(PermissionId, Arg.Any<CancellationToken>()).Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(PermissionId, CancellationToken.None))
            .Should().ThrowAsync<PermissionInUseException>();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    // AC — permiso no encontrado → PermissionNotFoundException
    [Fact]
    public async Task HandleAsync_NotFound_ThrowsPermissionNotFound()
    {
        _repo.ExistsAsync(PermissionId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(PermissionId, CancellationToken.None))
            .Should().ThrowAsync<PermissionNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().IsInUseByRolesAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }
}
