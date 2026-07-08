using Flit.Modules.Security.Application.UserManagement.SuspendUser;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserManagement;

/// <summary>
/// HU #10619: unificación de suspensión temporal/desactivación indefinida + fix de alcance
/// SuperAdmin (AC3) + guarda de último administrador activo (AC4).
/// </summary>
public sealed class SuspendUserHandlerTests
{
    private readonly IUserManagementRepository _repo = Substitute.For<IUserManagementRepository>();
    private readonly SuspendUserHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();
    private static readonly Guid SuspensionId = Guid.NewGuid();

    public SuspendUserHandlerTests()
    {
        _handler = new SuspendUserHandler(_repo);
        _repo.CreateSuspensionAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(),
                Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SuspensionId);
        _repo.GetActiveAdminRoleAssignmentsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private static SuspendUserCommand MakeCommand(DateTimeOffset? endsAt, bool callerIsSuperAdmin = false) =>
        new(TenantId, UserId, "Motivo de prueba", endsAt, CallerId, callerIsSuperAdmin);

    // AC1 — sin EndsAt: desactivación indefinida, se propaga null tal cual al repositorio.
    [Fact]
    public async Task HandleAsync_WithoutEndsAt_CreatesIndefiniteSuspension()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null, 1));

        var id = await _handler.HandleAsync(MakeCommand(endsAt: null), CancellationToken.None);

        id.Should().Be(SuspensionId);
        await _repo.Received(1).CreateSuspensionAsync(
            TenantId, UserId, Arg.Any<DateTimeOffset>(), null, "Motivo de prueba", CallerId, Arg.Any<CancellationToken>());
    }

    // AC2 — con EndsAt: comportamiento actual sin cambios, se propaga la fecha en UTC.
    [Fact]
    public async Task HandleAsync_WithEndsAt_CreatesTemporarySuspension()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null, 1));

        var endsAt = DateTimeOffset.UtcNow.AddDays(3);

        await _handler.HandleAsync(MakeCommand(endsAt), CancellationToken.None);

        await _repo.Received(1).CreateSuspensionAsync(
            TenantId, UserId, Arg.Any<DateTimeOffset>(), endsAt.ToUniversalTime(), "Motivo de prueba", CallerId,
            Arg.Any<CancellationToken>());
    }

    // AC3 — SuperAdmin actúa sobre el tenant REAL del usuario objetivo (distinto al propio),
    // sin que el scope-check lo bloquee ni fuerce su propio tenant.
    [Fact]
    public async Task HandleAsync_AsSuperAdmin_TargetingOtherTenant_UsesTargetTenant()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, OtherTenantId, "user@flit.local", "Usuario", null, 1));

        await _handler.HandleAsync(MakeCommand(endsAt: null, callerIsSuperAdmin: true), CancellationToken.None);

        await _repo.Received(1).CreateSuspensionAsync(
            OtherTenantId, UserId, Arg.Any<DateTimeOffset>(), null, Arg.Any<string>(), CallerId, Arg.Any<CancellationToken>());
    }

    // AC3 (bug corregido) — caller NO SuperAdmin intentando actuar sobre un usuario de otro tenant.
    [Fact]
    public async Task HandleAsync_AsNonSuperAdmin_TargetingOtherTenant_ThrowsUserOutOfScope()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, OtherTenantId, "user@flit.local", "Usuario", null, 1));

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(endsAt: null, callerIsSuperAdmin: false), CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateSuspensionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // AC4 (negativo) — nadie puede suspenderse/desactivarse a sí mismo.
    [Fact]
    public async Task HandleAsync_WhenSelfSuspension_ThrowsSelfSuspension()
    {
        _repo.FindTargetAsync(CallerId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(CallerId, TenantId, "self@flit.local", "Yo mismo", null, 1));

        var command = new SuspendUserCommand(TenantId, CallerId, "Motivo", null, CallerId, false);

        await _handler
            .Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<SelfSuspensionException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAdminRoleAssignmentsAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateSuspensionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // AC4 (negativo) — el objetivo es el único AdminCompany activo del tenant: se rechaza.
    [Fact]
    public async Task HandleAsync_WhenTargetIsLastActiveAdminCompany_ThrowsLastActiveAdmin()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "admin@flit.local", "Admin", null, 1));
        _repo.GetActiveAdminRoleAssignmentsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new ActiveAdminRoleAssignment(AdminRoleCodes.AdminCompany, TenantId)]);
        _repo.HasOtherActiveAdminsAsync(AdminRoleCodes.AdminCompany, TenantId, UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(endsAt: null), CancellationToken.None))
            .Should().ThrowAsync<LastActiveAdminException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateSuspensionAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    // AC4 (negativo) — el objetivo es el único SuperAdmin del sistema (alcance GLOBAL, scope null).
    [Fact]
    public async Task HandleAsync_WhenTargetIsLastActiveSuperAdmin_ThrowsLastActiveAdmin_WithGlobalScope()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "super@flit.local", "Super", null, 1));
        _repo.GetActiveAdminRoleAssignmentsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new ActiveAdminRoleAssignment(AdminRoleCodes.SuperAdmin, TenantId)]);
        _repo.HasOtherActiveAdminsAsync(AdminRoleCodes.SuperAdmin, null, UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(endsAt: null, callerIsSuperAdmin: true), CancellationToken.None))
            .Should().ThrowAsync<LastActiveAdminException>();

        await _repo.Received(1).HasOtherActiveAdminsAsync(
            AdminRoleCodes.SuperAdmin, null, UserId, Arg.Any<CancellationToken>());
    }

    // AC4 — hay OTRO admin disponible: la suspensión procede con normalidad.
    [Fact]
    public async Task HandleAsync_WhenAnotherActiveAdminExists_Succeeds()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "admin@flit.local", "Admin", null, 1));
        _repo.GetActiveAdminRoleAssignmentsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new ActiveAdminRoleAssignment(AdminRoleCodes.AdminCompany, TenantId)]);
        _repo.HasOtherActiveAdminsAsync(AdminRoleCodes.AdminCompany, TenantId, UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(endsAt: null), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).CreateSuspensionAsync(
            TenantId, UserId, Arg.Any<DateTimeOffset>(), null, Arg.Any<string>(), CallerId, Arg.Any<CancellationToken>());
    }

    // Usuario objetivo inexistente / eliminado → TargetUserNotFoundException.
    [Fact]
    public async Task HandleAsync_WhenTargetNotFound_ThrowsTargetUserNotFound()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns((UserManagementTarget?)null);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(endsAt: null), CancellationToken.None))
            .Should().ThrowAsync<TargetUserNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAdminRoleAssignmentsAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Suspender de nuevo cierra primero cualquier suspensión previa activa (idempotencia del reemplazo).
    [Fact]
    public async Task HandleAsync_ClosesActiveSuspensionsBeforeCreatingNewOne()
    {
        _repo.FindTargetAsync(UserId, false, Arg.Any<CancellationToken>())
            .Returns(new UserManagementTarget(UserId, TenantId, "user@flit.local", "Usuario", null, 1));

        await _handler.HandleAsync(MakeCommand(endsAt: null), CancellationToken.None);

        await _repo.Received(1).CloseActiveSuspensionsAsync(
            TenantId, UserId, Arg.Any<DateTimeOffset>(), CallerId, Arg.Any<CancellationToken>());
    }
}
