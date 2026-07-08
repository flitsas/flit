using Flit.Modules.Security.Application.UserManagement.RestoreUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserManagement;

/// <summary>
/// HU #10623: restaura (deshace el soft-delete) a un usuario eliminado. AC3: recupera exactamente
/// el mismo estado porque DeleteUserHandler nunca tocó UserRoleAssignment/UserTempSuspension —
/// aquí solo se valida que RestoreUserAsync se invoque, sin ningún efecto sobre esas tablas. AC5:
/// restaurar un usuario que NO está eliminado se rechaza explícitamente.
/// </summary>
public sealed class RestoreUserHandlerTests
{
    private readonly IUserManagementRepository _repo = Substitute.For<IUserManagementRepository>();
    private readonly RestoreUserHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    public RestoreUserHandlerTests()
    {
        _handler = new RestoreUserHandler(_repo);
    }

    private static RestoreUserCommand MakeCommand() => new(UserId, CallerId);

    // AC3 — usuario eliminado (DeletedAt != null) → se restaura, sin tocar roles/suspensión (ni
    // siquiera se consultan aquí: RestoreUserAsync es la única llamada al repositorio).
    [Fact]
    public async Task HandleAsync_WhenUserIsDeleted_RestoresUser()
    {
        _repo.FindTargetAsync(UserId, true, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", DateTimeOffset.UtcNow, 1));

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).RestoreUserAsync(UserId, CallerId, Arg.Any<CancellationToken>());
    }

    // AC5 — el usuario objetivo NO está eliminado (DeletedAt nulo): se rechaza explícitamente, no
    // es un no-op silencioso.
    [Fact]
    public async Task HandleAsync_WhenUserIsNotDeleted_ThrowsUserNotDeleted()
    {
        _repo.FindTargetAsync(UserId, true, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null, 1));

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<UserNotDeletedException>();

        await _repo.DidNotReceiveWithAnyArgs().RestoreUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // Usuario objetivo inexistente → TargetUserNotFoundException.
    [Fact]
    public async Task HandleAsync_WhenTargetNotFound_ThrowsTargetUserNotFound()
    {
        _repo.FindTargetAsync(UserId, true, Arg.Any<CancellationToken>())
            .Returns((UserManagementTarget?)null);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<TargetUserNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().RestoreUserAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
