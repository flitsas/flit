using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

public sealed class SetRolePermissionsHandlerTests
{
    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();
    private readonly SetRolePermissionsHandler _handler;

    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid PermissionId = Guid.NewGuid();

    private static RoleDetail MakeRole(IReadOnlyList<PermissionSlug>? permissions = null) =>
        new(RoleId, "COMPANY", "ADMIN", "Administrador", null, false, true, permissions ?? []);

    public SetRolePermissionsHandlerTests()
    {
        _handler = new SetRolePermissionsHandler(_repo);
    }

    // HU #10505 — reemplaza permisos de un rol global (ya no requiere tenantId)
    [Fact]
    public async Task HandleAsync_ExistingRole_ReplacesPermissions_AndReturnsUpdatedDetail()
    {
        var permissionIds = new List<Guid> { PermissionId };
        var updated = MakeRole([new PermissionSlug(PermissionId, "rbac.manage", "Administrar RBAC")]);

        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(MakeRole(), updated);

        var result = await _handler.HandleAsync(
            new SetRolePermissionsCommand(RoleId, permissionIds),
            CancellationToken.None);

        result.Should().Be(updated);
        await _repo.Received(1).SetPermissionsAsync(RoleId, permissionIds, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_RoleNotFound_ThrowsRoleNotFound_WithoutSettingPermissions()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns((RoleDetail?)null);

        await _handler
            .Invoking(h => h.HandleAsync(
                new SetRolePermissionsCommand(RoleId, [PermissionId]),
                CancellationToken.None))
            .Should().ThrowAsync<RoleNotFoundException>();

        await _repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    // HU #12964 — un rol solo tiene permisos de módulos de su producto (contrato v1 §4)
    [Fact]
    public async Task HandleAsync_PermissionFromAnotherProduct_ThrowsMismatch_WithoutSettingPermissions()
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleDetail(RoleId, "COMPANY", "Radicador", "Radicador", null, false, true, [], "tramites"));
        _repo.GetPermissionProductCodesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(["tramites", "plataforma"]);

        await _handler
            .Invoking(h => h.HandleAsync(new SetRolePermissionsCommand(RoleId, [PermissionId]), CancellationToken.None))
            .Should().ThrowAsync<RolePermissionProductMismatchException>();

        await _repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(
            Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }

    // HU #12964 — SuperAdmin conserva el bypass en todos los productos (contrato v1 §2.1)
    [Fact]
    public async Task HandleAsync_SuperAdmin_AcceptsPermissionsFromAnyProduct()
    {
        var superAdmin = new RoleDetail(RoleId, "COMPANY", "SuperAdmin", "SuperAdmin", null, true, true, [], "plataforma");
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(superAdmin, superAdmin);
        _repo.GetPermissionProductCodesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(["tramites", "plataforma"]);

        await _handler.HandleAsync(new SetRolePermissionsCommand(RoleId, [PermissionId]), CancellationToken.None);

        await _repo.Received(1).SetPermissionsAsync(RoleId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>());
    }
}
