using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles;

/// <summary>HU #13646 (FB-15, hallazgo de review): quitar un rol purga los permisos individuales del producto del rol quitado.</summary>
public sealed class RemoveRoleAssignmentHandlerPurgeTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid OtherRoleId = Guid.NewGuid();
    private static readonly Guid RemovedBy = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();

    private readonly IUserRoleAssignmentRepository _repo = Substitute.For<IUserRoleAssignmentRepository>();
    private readonly IRoleChangeOverridesPurger _purger = Substitute.For<IRoleChangeOverridesPurger>();
    private readonly RemoveRoleAssignmentHandler _handler;

    public RemoveRoleAssignmentHandlerPurgeTests()
    {
        _handler = new RemoveRoleAssignmentHandler(_repo, _purger);
        _repo.GetActiveAssignmentAsync(UserId, TenantId, RoleId, Arg.Any<CancellationToken>())
            .Returns(new UserRoleAssignmentSnapshot(AssignmentId, UserId, RoleId));
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<UserRoleAssignmentSnapshot> { new(AssignmentId, UserId, RoleId) });
        _repo.GetRoleProductCodeAsync(RoleId, Arg.Any<CancellationToken>()).Returns("tramites");
        _purger.RunAtomicallyAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task>>()());
    }

    private Task Remove() => _handler.HandleAsync(UserId, TenantId, RoleId, RemovedBy, CancellationToken.None);

    [Fact]
    public async Task Quitar_el_unico_rol_del_producto_purga_los_overrides_dentro_de_la_transaccion()
    {
        await Remove();

        await _purger.Received(1).RunAtomicallyAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SoftDeleteAssignmentAsync(AssignmentId, RemovedBy, Arg.Any<CancellationToken>());
        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", RemovedBy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_queda_otro_rol_activo_del_mismo_producto_no_purga()
    {
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(new List<UserRoleAssignmentSnapshot>
        {
            new(AssignmentId, UserId, RoleId), new(Guid.NewGuid(), UserId, OtherRoleId),
        });
        _repo.GetActiveRoleAsync(OtherRoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(OtherRoleId, "COMPANY", null, "tramites"));

        await Remove();

        await _repo.Received(1).SoftDeleteAssignmentAsync(AssignmentId, RemovedBy, Arg.Any<CancellationToken>());
        await _purger.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, default!, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_rol_restante_de_otro_producto_no_evita_la_purga()
    {
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(new List<UserRoleAssignmentSnapshot>
        {
            new(AssignmentId, UserId, RoleId), new(Guid.NewGuid(), UserId, OtherRoleId),
        });
        _repo.GetActiveRoleAsync(OtherRoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(OtherRoleId, "COMPANY", null, "comparendos"));

        await Remove();

        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", RemovedBy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_rol_restante_inactivo_no_cuenta_y_se_purga()
    {
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(new List<UserRoleAssignmentSnapshot>
        {
            new(AssignmentId, UserId, RoleId), new(Guid.NewGuid(), UserId, OtherRoleId),
        });
        _repo.GetActiveRoleAsync(OtherRoleId, Arg.Any<CancellationToken>()).Returns((RoleForAssignmentSnapshot?)null);

        await Remove();

        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", RemovedBy, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Producto_nulo_o_vacio_se_trata_como_tramites(string? product)
    {
        _repo.GetRoleProductCodeAsync(RoleId, Arg.Any<CancellationToken>()).Returns(product);

        await Remove();

        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", RemovedBy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Producto_distinto_se_purga_con_su_codigo()
    {
        _repo.GetRoleProductCodeAsync(RoleId, Arg.Any<CancellationToken>()).Returns("comparendos");

        await Remove();

        await _purger.Received(1).PurgeAsync(TenantId, UserId, "comparendos", RemovedBy, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_la_baja_falla_no_se_purga()
    {
        _repo.SoftDeleteAssignmentAsync(AssignmentId, RemovedBy, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("boom"));

        await FluentActions.Awaiting(Remove).Should().ThrowAsync<InvalidOperationException>();

        await _purger.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, default!, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_asignacion_activa_lanza_not_found_y_no_purga()
    {
        _repo.GetActiveAssignmentAsync(UserId, TenantId, RoleId, Arg.Any<CancellationToken>()).Returns((UserRoleAssignmentSnapshot?)null);

        await FluentActions.Awaiting(Remove).Should().ThrowAsync<RoleAssignmentNotFoundException>();

        await _purger.DidNotReceiveWithAnyArgs().RunAtomicallyAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_purger_registrado_la_baja_funciona_igual()
    {
        await new RemoveRoleAssignmentHandler(_repo).HandleAsync(UserId, TenantId, RoleId, RemovedBy, CancellationToken.None);

        await _repo.Received(1).SoftDeleteAssignmentAsync(AssignmentId, RemovedBy, Arg.Any<CancellationToken>());
    }
}
