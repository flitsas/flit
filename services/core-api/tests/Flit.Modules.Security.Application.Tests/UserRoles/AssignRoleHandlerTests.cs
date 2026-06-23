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
    private static readonly Guid AssignmentId = Guid.NewGuid();

    private static AssignRoleCommand MakeCommand() =>
        new(TenantId, UserId, RoleId, CallerId);

    public AssignRoleHandlerTests()
    {
        _handler = new AssignRoleHandler(_repo);
        _repo.CreateAssignmentAsync(Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>())
            .Returns(Guid.CreateVersion7());
    }

    // AC1 — usuario sin rol previo → crea assignment → 200
    [Fact]
    public async Task HandleAsync_WhenNoExistingRole_CreatesAssignment()
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.RoleIsActiveInTenantAsync(RoleId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetActiveAssignmentAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns((UserRoleAssignmentSnapshot?)null);

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

    // AC2 — usuario ya tiene rol activo → soft-delete del anterior, crea nuevo → 200
    [Fact]
    public async Task HandleAsync_WhenAlreadyHasRole_SoftDeletesOldAndCreatesNew()
    {
        var existingSnapshot = new UserRoleAssignmentSnapshot(AssignmentId, UserId, Guid.NewGuid());
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.RoleIsActiveInTenantAsync(RoleId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetActiveAssignmentAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(existingSnapshot);

        await _handler.Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().NotThrowAsync();

        await _repo.Received(1).SoftDeleteAssignmentAsync(
            AssignmentId, CallerId, Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAssignmentAsync(
            Arg.Is<AssignRoleData>(d =>
                d.TenantId == TenantId &&
                d.UserId == UserId &&
                d.RoleId == RoleId &&
                d.AssignedBy == CallerId),
            Arg.Any<CancellationToken>());
    }

    // AC3 — usuario pertenece a otro tenant → UserOutOfScopeException, sin tocar repo
    [Fact]
    public async Task HandleAsync_WhenUserBelongsToOtherTenant_ThrowsUserOutOfScope()
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<UserOutOfScopeException>();

        await _repo.DidNotReceiveWithAnyArgs().RoleIsActiveInTenantAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().GetActiveAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // AC4 — rol no existe o inactivo → RoleForAssignmentNotFoundException, sin crear assignment
    [Fact]
    public async Task HandleAsync_WhenRoleNotFound_ThrowsRoleForAssignmentNotFound()
    {
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.RoleIsActiveInTenantAsync(RoleId, TenantId, Arg.Any<CancellationToken>()).Returns(false);

        await _handler
            .Invoking(h => h.HandleAsync(MakeCommand(), CancellationToken.None))
            .Should().ThrowAsync<RoleForAssignmentNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().GetActiveAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _repo.DidNotReceiveWithAnyArgs().CreateAssignmentAsync(
            Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // AC2 extra — al reasignar el soft-delete recibe callerId (deletedBy correcto)
    [Fact]
    public async Task HandleAsync_WhenReplacing_SoftDeleteUsesCallerIdAsDeletedBy()
    {
        var existingSnapshot = new UserRoleAssignmentSnapshot(AssignmentId, UserId, Guid.NewGuid());
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.RoleIsActiveInTenantAsync(RoleId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetActiveAssignmentAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(existingSnapshot);

        await _handler.HandleAsync(MakeCommand(), CancellationToken.None);

        await _repo.Received(1).SoftDeleteAssignmentAsync(
            AssignmentId,
            Arg.Is<Guid>(id => id == CallerId),
            Arg.Any<CancellationToken>());
    }
}
