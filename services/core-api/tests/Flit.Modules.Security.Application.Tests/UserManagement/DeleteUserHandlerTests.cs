using Flit.Modules.Security.Application.UserManagement.DeleteUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserManagement;

/// <summary>
/// HU #10623: eliminar (soft-delete reversible) a un usuario + guarda de auto-eliminación (AC2) y
/// de último administrador activo (AC2, reutilizada de HU #10619 AC4).
/// </summary>
public sealed class DeleteUserHandlerTests
{
    private readonly IUserManagementRepository _repo = Substitute.For<IUserManagementRepository>();
    private readonly DeleteUserHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private const long RowVersion = 5L;

    public DeleteUserHandlerTests()
    {
        _handler = new DeleteUserHandler(_repo);
        _repo.GetActiveAdminRoleAssignmentsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private static DeleteUserCommand MakeCommand(bool callerIsSuperAdmin = false) =>
        new(TenantId, UserId, RowVersion, CallerId, callerIsSuperAdmin);

    // AC1 — usuario del propio alcance, sin roles administrativos: se marca DeletedAt/DeletedBy.
    [Fact]
    public async Task HandleAsync_WithinScope_SoftDeletesUser()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null, RowVersion));

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteUserAsync(
            UserId, Arg.Any<DateTimeOffset>(), CallerId, RowVersion, Arg.Any<CancellationToken>());
    }

    // AC — SuperAdmin actúa sobre el tenant REAL del usuario objetivo (distinto al propio).
    [Fact]
    public async Task HandleAsync_AsSuperAdmin_TargetingOtherTenant_Succeeds()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, OtherTenantId, "user@flit.local", "Usuario", null, RowVersion));

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: true), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteUserAsync(
            UserId, Arg.Any<DateTimeOffset>(), CallerId, RowVersion, Arg.Any<CancellationToken>());
    }

    // Caller NO SuperAdmin intentando eliminar un usuario de otro tenant → UserOutOfScopeException.
    [Fact]
    public async Task HandleAsync_AsNonSuperAdmin_TargetingOtherTenant_ThrowsUserOutOfScope()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, OtherTenantId, "user@flit.local", "Usuario", null, RowVersion));

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: false), CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteUserAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // AC2 — un usuario no puede eliminarse a sí mismo.
    [Fact]
    public async Task HandleAsync_WhenSelfDeletion_ThrowsSelfDeletion()
    {
        _repo.FindTargetAsync(CallerId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(CallerId, TenantId, "self@flit.local", "Yo mismo", null, RowVersion));

        var command = new DeleteUserCommand(TenantId, CallerId, RowVersion, CallerId, false);

        await _handler
            .Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<SelfDeletionException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAdminRoleAssignmentsAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteUserAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // AC2 — el objetivo es el único AdminCompany activo del tenant: se rechaza.
    [Fact]
    public async Task HandleAsync_WhenTargetIsLastActiveAdminCompany_ThrowsLastActiveAdmin()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "admin@flit.local", "Admin", null, RowVersion));
        _repo.GetActiveAdminRoleAssignmentsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new ActiveAdminRoleAssignment(AdminRoleCodes.AdminCompany, TenantId)]);
        _repo.HasOtherActiveAdminsAsync(AdminRoleCodes.AdminCompany, TenantId, UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<LastActiveAdminException>();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteUserAsync(
            Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<Guid?>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    // AC2 — el objetivo es el único SuperAdmin del sistema (alcance GLOBAL, scope null).
    [Fact]
    public async Task HandleAsync_WhenTargetIsLastActiveSuperAdmin_ThrowsLastActiveAdmin_WithGlobalScope()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "super@flit.local", "Super", null, RowVersion));
        _repo.GetActiveAdminRoleAssignmentsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new ActiveAdminRoleAssignment(AdminRoleCodes.SuperAdmin, TenantId)]);
        _repo.HasOtherActiveAdminsAsync(AdminRoleCodes.SuperAdmin, null, UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(callerIsSuperAdmin: true), CancellationToken.None))
            .Should().ThrowAsync<LastActiveAdminException>();

        await _repo.Received(1).HasOtherActiveAdminsAsync(
            AdminRoleCodes.SuperAdmin, null, UserId, Arg.Any<CancellationToken>());
    }

    // Hay OTRO admin disponible: la eliminación procede con normalidad.
    [Fact]
    public async Task HandleAsync_WhenAnotherActiveAdminExists_Succeeds()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "admin@flit.local", "Admin", null, RowVersion));
        _repo.GetActiveAdminRoleAssignmentsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new ActiveAdminRoleAssignment(AdminRoleCodes.AdminCompany, TenantId)]);
        _repo.HasOtherActiveAdminsAsync(AdminRoleCodes.AdminCompany, TenantId, UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteUserAsync(
            UserId, Arg.Any<DateTimeOffset>(), CallerId, RowVersion, Arg.Any<CancellationToken>());
    }

    // Usuario objetivo inexistente / ya eliminado → TargetUserNotFoundException.
    [Fact]
    public async Task HandleAsync_WhenTargetNotFound_ThrowsTargetUserNotFound()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns((UserManagementTarget?)null);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<TargetUserNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAdminRoleAssignmentsAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
