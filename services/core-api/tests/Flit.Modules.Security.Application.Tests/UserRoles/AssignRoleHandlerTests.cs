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
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(existing is null ? [] : new List<UserRoleAssignmentSnapshot> { existing });
    }

    // Un usuario tiene UN rol: asignar uno nuevo CIERRA el anterior. Lo que define lo que puede
    // hacer son los permisos de ese rol, no acumular roles (revierte el aditivo de HU #10506).
    [Fact]
    public async Task HandleAsync_ReplacesPreviousRoleInsteadOfAddingASecondOne()
    {
        var previous = new UserRoleAssignmentSnapshot(Guid.NewGuid(), UserId, Guid.NewGuid());
        SetupHappyPath(existing: previous);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteAssignmentAsync(
            previous.Id, CallerId, Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAssignmentAsync(
            Arg.Is<AssignRoleData>(d =>
                d.TenantId == TenantId &&
                d.UserId == UserId &&
                d.RoleId == RoleId &&
                d.AssignedBy == CallerId),
            Arg.Any<CancellationToken>());
    }

    // Datos anteriores al rol único: si el usuario arrastra varios roles activos, se cierran todos.
    [Fact]
    public async Task HandleAsync_ClosesEveryPreviousRole_WhenLegacyMultiRoleDataExists()
    {
        var first = new UserRoleAssignmentSnapshot(Guid.NewGuid(), UserId, Guid.NewGuid());
        var second = new UserRoleAssignmentSnapshot(Guid.NewGuid(), UserId, Guid.NewGuid());
        SetupHappyPath(existing: null);
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<UserRoleAssignmentSnapshot> { first, second });

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteAssignmentAsync(first.Id, CallerId, Arg.Any<CancellationToken>());
        await _repo.Received(1).SoftDeleteAssignmentAsync(second.Id, CallerId, Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // Sin rol previo: no hay nada que cerrar.
    [Fact]
    public async Task HandleAsync_WhenUserHasNoRole_AssignsWithoutClosingAnything()
    {
        SetupHappyPath(existing: null);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // Pedir el rol que el usuario YA tiene no es un cambio: se rechaza antes de cerrar nada,
    // para no dejarlo sin rol si algo fallara al recrearlo.
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

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAssignmentsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAssignmentsAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
