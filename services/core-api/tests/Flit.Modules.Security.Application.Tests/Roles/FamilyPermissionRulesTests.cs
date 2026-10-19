using Flit.Modules.Security.Application.Roles;
using Flit.Modules.Security.Domain.Roles;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Roles;

/// <summary>
/// HU #13664 (Feature #13636 F-F, Épica #12753) AC1 — «crear exige ver» por familia de trámite en la configuración de roles:
/// otorgar <c>traspasos.create</c> sin <c>traspasos.read</c> lanza <see cref="FamilyCreateRequiresReadException"/> (400) y quitar
/// <c>traspasos.read</c> retira también <c>traspasos.create</c>. La regla aplica siempre (es validación de datos, no depende del flag de exigencia en runtime).
/// <para>Uso de ejemplo: <c>FamilyPermissionRules.Resolve(actuales, pedidos).Violations</c> es reutilizable por la configuración por usuario (F-B).</para>
/// </summary>
public sealed class FamilyPermissionRulesTests
{
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly PermissionInfo Read = new(Guid.NewGuid(), "traspasos.read", "Ver traspasos", "tramites-traspasos", "tramites");
    private static readonly PermissionInfo Create = new(Guid.NewGuid(), "traspasos.create", "Crear traspasos", "tramites-traspasos", "tramites");
    private static readonly string[] Held = ["traspasos.read", "traspasos.create"];

    private readonly IRoleRepository _repo = Substitute.For<IRoleRepository>();

    private static HashSet<string> S(params string[] slugs) => new(slugs, StringComparer.OrdinalIgnoreCase);

    private static RoleDetail Role(params PermissionInfo[] tiene) =>
        new(RoleId, "COMPANY", "gestor", "Gestor", null, false, true,
            [.. tiene.Select(p => new PermissionSlug(p.Id, p.Slug, p.Name))], "tramites", TenantId);

    private void Arrange(RoleDetail role)
    {
        _repo.GetByIdAsync(RoleId, Arg.Any<CancellationToken>()).Returns(role);
        _repo.GetVisibleToTenantAsync(TenantId, RoleId, Arg.Any<CancellationToken>()).Returns(role);
        _repo.GetEnabledProductCodesAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new HashSet<string> { "tramites" });
        _repo.GetPermissionInfosAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => new[] { Read, Create }.Where(i => call.Arg<IReadOnlyList<Guid>>().Contains(i.Id)).ToList());
        _repo.GetPermissionProductCodesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns(["tramites"]);
    }

    [Fact]
    public void Regla_CrearSinVer_EsViolacion()
    {
        var r = FamilyPermissionRules.Resolve(S(), S("traspasos.create"));

        r.Violations.Should().Equal("traspasos.create");
        r.CascadeDropped.Should().BeEmpty();
    }

    [Fact]
    public void Regla_QuitarVerQueSeTenia_ArrastraElCrear()
    {
        var r = FamilyPermissionRules.Resolve(S("traspasos.read", "traspasos.create"), S("traspasos.create"));

        r.Violations.Should().BeEmpty();
        r.CascadeDropped.Should().Equal("traspasos.create");
    }

    [Fact]
    public void Regla_ParCompletoOSoloVer_EsValido()
    {
        FamilyPermissionRules.Resolve(S(), S("matriculas.read", "matriculas.create", "traspasos.read")).Violations.Should().BeEmpty();
        FamilyPermissionRules.Resolve(S(), S("tramites.create")).Violations.Should().BeEmpty("solo gobierna los slugs por familia");
    }

    // AC1 (rol global, SuperAdmin/RbacAdmin)
    [Fact]
    public async Task AC1_RolGlobal_OtorgarCrearSinVer_Lanza()
    {
        Arrange(Role());

        var act = () => new SetRolePermissionsHandler(_repo)
            .HandleAsync(new SetRolePermissionsCommand(RoleId, [Create.Id]), CancellationToken.None);

        (await act.Should().ThrowAsync<FamilyCreateRequiresReadException>()).Which.Slugs.Should().Equal("traspasos.create");
        await _repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_RolGlobal_QuitarVer_QuitaTambienCrear()
    {
        Arrange(Role(Read, Create));

        await new SetRolePermissionsHandler(_repo)
            .HandleAsync(new SetRolePermissionsCommand(RoleId, [Create.Id]), CancellationToken.None);

        await _repo.Received(1).SetPermissionsAsync(
            RoleId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    // AC1 (rol propio de la compañía)
    [Fact]
    public async Task AC1_RolDeCompania_OtorgarCrearSinVer_Lanza()
    {
        Arrange(Role());

        var act = () => new SetTenantRolePermissionsHandler(_repo)
            .HandleAsync(new SetTenantRolePermissionsCommand(TenantId, RoleId, [Create.Id], Held), CancellationToken.None);

        await act.Should().ThrowAsync<FamilyCreateRequiresReadException>();
        await _repo.DidNotReceiveWithAnyArgs().SetPermissionsAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_RolDeCompania_QuitarVer_QuitaTambienCrear()
    {
        Arrange(Role(Read, Create));

        await new SetTenantRolePermissionsHandler(_repo)
            .HandleAsync(new SetTenantRolePermissionsCommand(TenantId, RoleId, [Create.Id], Held), CancellationToken.None);

        await _repo.Received(1).SetPermissionsAsync(
            RoleId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_RolDeCompania_ParCompleto_SeGuardaIntacto()
    {
        Arrange(Role());

        await new SetTenantRolePermissionsHandler(_repo)
            .HandleAsync(new SetTenantRolePermissionsCommand(TenantId, RoleId, [Read.Id, Create.Id], Held), CancellationToken.None);

        await _repo.Received(1).SetPermissionsAsync(
            RoleId, Arg.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
    }
}
