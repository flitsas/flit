using Flit.Modules.Security.Application.UserManagement.UnsuspendUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserManagement;

public sealed class UnsuspendUserHandlerTests
{
    private readonly IUserManagementRepository _repo = Substitute.For<IUserManagementRepository>();
    private readonly UnsuspendUserHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    public UnsuspendUserHandlerTests()
    {
        _handler = new UnsuspendUserHandler(_repo);
    }

    private static UnsuspendUserCommand MakeCommand(bool callerIsSuperAdmin = false) =>
        new(TenantId, UserId, CallerId, callerIsSuperAdmin);

    // AC5 (borde) — reactivación de un usuario que estaba desactivado indefinidamente
    // (o con suspensión temporal vigente): levanta la restricción con normalidad.
    [Fact]
    public async Task HandleAsync_WhenActiveSuspensionExists_ClosesIt()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null));
        _repo.CloseActiveSuspensionsAsync(TenantId, UserId, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>())
            .Returns(1);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).CloseActiveSuspensionsAsync(
            TenantId, UserId, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }

    // No había ninguna suspensión activa que levantar → NoActiveSuspensionException.
    [Fact]
    public async Task HandleAsync_WhenNoActiveSuspension_ThrowsNoActiveSuspension()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null));
        _repo.CloseActiveSuspensionsAsync(TenantId, UserId, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>())
            .Returns(0);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<NoActiveSuspensionException>();
    }

    // AC3 — SuperAdmin reactiva un usuario de un tenant distinto al propio: aplica sobre el
    // tenant REAL del objetivo, sin el bug de forzar el tenant del caller.
    [Fact]
    public async Task HandleAsync_AsSuperAdmin_TargetingOtherTenant_UsesTargetTenant()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, OtherTenantId, "user@flit.local", "Usuario", null));
        _repo.CloseActiveSuspensionsAsync(OtherTenantId, UserId, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>())
            .Returns(1);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: true), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).CloseActiveSuspensionsAsync(
            OtherTenantId, UserId, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }

    // AC3 (bug corregido) — caller NO SuperAdmin intentando reactivar un usuario de otro tenant.
    [Fact]
    public async Task HandleAsync_AsNonSuperAdmin_TargetingOtherTenant_ThrowsUserOutOfScope()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, OtherTenantId, "user@flit.local", "Usuario", null));

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: false), CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();

        await _repo.DidNotReceiveWithAnyArgs().CloseActiveSuspensionsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // Usuario objetivo inexistente / eliminado → TargetUserNotFoundException.
    [Fact]
    public async Task HandleAsync_WhenTargetNotFound_ThrowsTargetUserNotFound()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns((UserManagementTarget?)null);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<TargetUserNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().CloseActiveSuspensionsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
