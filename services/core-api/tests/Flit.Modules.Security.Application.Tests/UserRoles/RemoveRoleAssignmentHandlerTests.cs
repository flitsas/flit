using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles;

public sealed class RemoveRoleAssignmentHandlerTests
{
    private readonly IUserRoleAssignmentRepository _repo = Substitute.For<IUserRoleAssignmentRepository>();
    private readonly RemoveRoleAssignmentHandler _handler;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid RemovedBy = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();

    public RemoveRoleAssignmentHandlerTests()
    {
        _handler = new RemoveRoleAssignmentHandler(_repo);
    }

    // AC3 — quita un rol puntual sin afectar los demás roles del usuario: solo soft-elimina
    // LA asignación exacta (userId, tenantId, roleId), sin tocar ninguna otra.
    [Fact]
    public async Task HandleAsync_WhenActiveAssignmentExists_SoftDeletesOnlyThatAssignment()
    {
        var existing = new UserRoleAssignmentSnapshot(AssignmentId, UserId, RoleId);
        _repo.GetActiveAssignmentAsync(UserId, TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(existing);

        await _handler.HandleAsync(UserId, TenantId, RoleId, RemovedBy, CancellationToken.None);

        await _repo.Received(1).SoftDeleteAssignmentAsync(AssignmentId, RemovedBy, Arg.Any<CancellationToken>());
    }

    // Sin asignación activa de ese rol puntual → RoleAssignmentNotFoundException, sin tocar el repo
    [Fact]
    public async Task HandleAsync_WhenNoActiveAssignment_ThrowsRoleAssignmentNotFound()
    {
        _repo.GetActiveAssignmentAsync(UserId, TenantId, RoleId, Arg.Any<CancellationToken>())
            .Returns((UserRoleAssignmentSnapshot?)null);

        await _handler
            .Invoking(h => h.HandleAsync(UserId, TenantId, RoleId, RemovedBy, CancellationToken.None))
            .Should().ThrowAsync<RoleAssignmentNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().SoftDeleteAssignmentAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
