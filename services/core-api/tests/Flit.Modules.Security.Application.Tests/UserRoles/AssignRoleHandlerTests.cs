using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles;

public sealed class AssignRoleHandlerTests
{
    private readonly IUserRoleAssignmentRepository _repo = Substitute.For<IUserRoleAssignmentRepository>();
    private readonly AssignRoleHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private static AssignRoleCommand MakeCommand() =>
        new(TenantId, UserId, RoleId, CallerId);

    public AssignRoleHandlerTests()
    {
        _handler = new AssignRoleHandler(_repo);
        _repo.CreateAssignmentAsync(Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>())
            .Returns(Guid.CreateVersion7());
    }

    private void SetupHappyPath(UserRoleAssignmentSnapshot? existing)
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetActiveRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(RoleId, "COMPANY"));
        _repo.GetTenantTargetEntityTypeAsync(TenantId, Arg.Any<CancellationToken>()).Returns("COMPANY");
        _repo.GetActiveAssignmentAsync(UserId, TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(existing);
    }

    // AC1 — usuario ya tiene otro rol activo, se le asigna un segundo rol distinto → queda con
    // AMBOS roles activos (modelo aditivo, no reemplaza ni toca el existente).
    [Fact]
    public async Task HandleAsync_AssignsSecondRole_AdditivelyWithoutTouchingExisting()
    {
        SetupHappyPath(existing: null);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAssignmentAsync(
            Arg.Is<AssignRoleData>(d =>
                d.TenantId == TenantId &&
                d.UserId == UserId &&
                d.RoleId == RoleId &&
                d.AssignedBy == CallerId),
            Arg.Any<CancellationToken>());
    }

    // AC2 — el mismo rol ya está activamente asignado → RoleAlreadyAssignedException, sin
    // duplicar fila ni tocar la existente.
    [Fact]
    public async Task HandleAsync_WhenRoleAlreadyActivelyAssigned_ThrowsRoleAlreadyAssigned()
    {
        var existingSnapshot = new UserRoleAssignmentSnapshot(Guid.NewGuid(), UserId, RoleId);
        SetupHappyPath(existing: existingSnapshot);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<RoleAlreadyAssignedException>();

        await _repo.DidNotReceiveWithAnyArgs().CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // usuario pertenece a otro tenant → UserOutOfScopeException, sin tocar el resto del repo
    [Fact]
    public async Task HandleAsync_WhenUserBelongsToOtherTenant_ThrowsUserOutOfScope()
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveRoleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // rol no existe o inactivo → RoleForAssignmentNotFoundException, sin crear assignment
    [Fact]
    public async Task HandleAsync_WhenRoleNotFound_ThrowsRoleForAssignmentNotFound()
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetActiveRoleAsync(RoleId, Arg.Any<CancellationToken>()).Returns((RoleForAssignmentSnapshot?)null);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<RoleForAssignmentNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // HU #10506 — rol TRANSIT_OFFICE asignado en un tenant COMPANY → RoleTargetEntityTypeMismatchException
    [Fact]
    public async Task HandleAsync_WhenRoleTargetEntityTypeMismatchesTenant_ThrowsMismatch()
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetActiveRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(RoleId, "TRANSIT_OFFICE"));
        _repo.GetTenantTargetEntityTypeAsync(TenantId, Arg.Any<CancellationToken>()).Returns("COMPANY");

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<RoleTargetEntityTypeMismatchException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // no auto-asignación (invariante preexistente, sin cambios en HU #10506)
    [Fact]
    public async Task HandleAsync_WhenSelfAssignment_ThrowsSelfRoleAssignment()
    {
        var command = new AssignRoleCommand(TenantId, CallerId, RoleId, CallerId);

        await _handler
            .Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<SelfRoleAssignmentException>();

        await _repo.DidNotReceiveWithAnyArgs().UserBelongsToTenantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // El SuperAdmin administra usuarios de cualquier tenant y el suyo es el interno de FLIT, que
    // nunca coincide con el del usuario destino: el alcance debe salir del USUARIO. Antes esto
    // devolvía OUT_OF_SCOPE en cuanto un SuperAdmin cambiaba el rol de un usuario de una empresa.
    [Fact]
    public async Task HandleAsync_AsSuperAdmin_UsesTargetUserTenantInsteadOfCallerTenant()
    {
        var flitTenantId = Guid.NewGuid();
        _repo.GetUserTenantAsync(UserId, Arg.Any<CancellationToken>()).Returns(TenantId);
        SetupHappyPath(existing: null);

        var command = new AssignRoleCommand(flitTenantId, UserId, RoleId, CallerId, CallerIsSuperAdmin: true);

        await _handler.Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).CreateAssignmentAsync(
            Arg.Is<AssignRoleData>(d => d.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_AsSuperAdmin_WhenTargetUserHasNoTenant_ThrowsOutOfScope()
    {
        _repo.GetUserTenantAsync(UserId, Arg.Any<CancellationToken>()).Returns((Guid?)null);

        var command = new AssignRoleCommand(Guid.NewGuid(), UserId, RoleId, CallerId, CallerIsSuperAdmin: true);

        await _handler
            .Invoking(h => h.HandleAsync(command, CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();
    }
}
