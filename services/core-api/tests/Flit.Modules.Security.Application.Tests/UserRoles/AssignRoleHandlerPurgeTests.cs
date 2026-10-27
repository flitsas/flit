using Flit.Modules.Security.Application.UserRoles;
using Flit.Modules.Security.Domain.PermissionOverrides;
using Flit.Modules.Security.Domain.UserRoles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.UserRoles;

/// <summary>HU #13646 (FB-15): el cambio de rol purga los permisos individuales del producto del rol nuevo y de los roles que cierra, atómicamente.</summary>
public sealed class AssignRoleHandlerPurgeTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid CallerId = Guid.NewGuid();

    private readonly IUserRoleAssignmentRepository _repo = Substitute.For<IUserRoleAssignmentRepository>();
    private readonly IRoleChangeOverridesPurger _purger = Substitute.For<IRoleChangeOverridesPurger>();
    private readonly AssignRoleHandler _handler;

    public AssignRoleHandlerPurgeTests()
    {
        _handler = new AssignRoleHandler(_repo, _purger);
        _repo.UserBelongsToTenantAsync(UserId, TenantId, Arg.Any<CancellationToken>()).Returns(true);
        _repo.GetTenantTargetEntityTypeAsync(TenantId, Arg.Any<CancellationToken>()).Returns("COMPANY");
        _repo.GetActiveRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(RoleId, "COMPANY", null, "tramites"));
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<UserRoleAssignmentSnapshot> { new(Guid.NewGuid(), UserId, Guid.NewGuid()) });
        _repo.CreateAssignmentAsync(Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        _purger.RunAtomicallyAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<Func<Task>>()());
    }

    private static AssignRoleCommand Command() => new(TenantId, UserId, RoleId, CallerId);

    [Fact]
    public async Task Cambio_de_rol_purga_los_permisos_individuales_del_producto_del_rol_nuevo_dentro_de_la_transaccion()
    {
        await _handler.HandleAsync(Command(), CancellationToken.None);

        await _purger.Received(1).RunAtomicallyAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", CallerId, Arg.Any<CancellationToken>());
        await _repo.Received(1).CreateAssignmentAsync(Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    // Hallazgo de la prueba funcional: Radicador (tramites) → AdminCompany (plataforma). El reemplazo cierra el rol de tramites,
    // así que sus permisos individuales también se purgan, no solo los del producto del rol nuevo.
    [Fact]
    public async Task Cambio_a_un_rol_de_otro_producto_purga_tambien_el_producto_del_rol_que_se_cierra()
    {
        var rolAnterior = Guid.NewGuid();
        _repo.GetActiveRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(RoleId, "COMPANY", null, "plataforma"));
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<UserRoleAssignmentSnapshot> { new(Guid.NewGuid(), UserId, rolAnterior) });
        _repo.GetRoleProductCodeAsync(rolAnterior, Arg.Any<CancellationToken>()).Returns("tramites");

        await _handler.HandleAsync(Command(), CancellationToken.None);

        await _purger.Received(1).PurgeAsync(TenantId, UserId, "plataforma", CallerId, Arg.Any<CancellationToken>());
        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", CallerId, Arg.Any<CancellationToken>());
        await _purger.Received(1).RunAtomicallyAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rol_ya_asignado_no_es_un_cambio_y_no_purga()
    {
        _repo.GetActiveAssignmentsAsync(UserId, TenantId, Arg.Any<CancellationToken>())
            .Returns(new List<UserRoleAssignmentSnapshot> { new(Guid.NewGuid(), UserId, RoleId) });

        await _handler.Invoking(h => h.HandleAsync(Command(), CancellationToken.None))
            .Should().ThrowAsync<RoleAlreadyAssignedException>();

        await _purger.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, default!, default, Arg.Any<CancellationToken>());
        await _purger.DidNotReceiveWithAnyArgs().RunAtomicallyAsync(default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_el_cambio_falla_no_se_purga()
    {
        _repo.CreateAssignmentAsync(Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>())
            .Returns<Guid>(_ => throw new InvalidOperationException("boom"));

        await _handler.Invoking(h => h.HandleAsync(Command(), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        await _purger.DidNotReceiveWithAnyArgs().PurgeAsync(default, default, default!, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_purger_registrado_el_cambio_de_rol_funciona_igual()
    {
        var handler = new AssignRoleHandler(_repo);

        await handler.HandleAsync(Command(), CancellationToken.None);

        await _repo.Received(1).CreateAssignmentAsync(Arg.Any<AssignRoleData>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Rol_nuevo_sin_producto_se_purga_como_tramites_y_no_en_silencio(string? product)
    {
        _repo.GetActiveRoleAsync(RoleId, Arg.Any<CancellationToken>())
            .Returns(new RoleForAssignmentSnapshot(RoleId, "COMPANY", null, product));

        await _handler.HandleAsync(Command(), CancellationToken.None);

        await _purger.Received(1).PurgeAsync(TenantId, UserId, "tramites", CallerId, Arg.Any<CancellationToken>());
    }
}
