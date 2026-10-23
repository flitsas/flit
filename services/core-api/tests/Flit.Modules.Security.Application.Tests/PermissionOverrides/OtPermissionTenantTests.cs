using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>
/// Decisión del PO tras la prueba funcional de la Épica #12753: los permisos OT (<c>ot.*</c>) no se ofrecen ni se otorgan a
/// usuarios de una compañía; en ellos no tendrían efecto (la política OtModule rechaza la ruta igual).
/// </summary>
public sealed class OtPermissionTenantTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid SuperAdminId = Guid.NewGuid();
    private static readonly PermissionCatalogEntry OtTramites = new(Guid.NewGuid(), "ot.tramites.manage", "OT", "ot-tramites", "tramites", "true");
    private static readonly PermissionCatalogEntry LogQx = new(Guid.NewGuid(), "logqx.read", "Log QX", "logqx", "plataforma", "true");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IOverrideTargetDirectory _dir = Substitute.For<IOverrideTargetDirectory>();
    private readonly IUserPermissionOverrideRepository _repo = Substitute.For<IUserPermissionOverrideRepository>();

    private static PermissionOverrideRequest Req(string action, string? slug) =>
        new(Tenant, new OverrideActor(SuperAdminId, true), Guid.NewGuid(), action, slug, "true");

    [Theory]
    [InlineData("GRANT", "ot.tramites.manage", false, "OT_NOT_APPLICABLE")]
    [InlineData("DENY", "ot.tramites.manage", false, "OT_NOT_APPLICABLE")]
    [InlineData("MODULE_SNAPSHOT", "ot-tramites", false, "OT_NOT_APPLICABLE")]
    [InlineData("REVOKE", "ot.tramites.manage", false, null)]
    [InlineData("READ", null, false, null)]
    [InlineData("GRANT", "logqx.read", false, null)]
    [InlineData("GRANT", "ot.tramites.manage", true, null)]
    public async Task La_regla_rechaza_permisos_OT_solo_fuera_de_un_organismo(string action, string? slug, bool esOrganismo, string? esperado)
    {
        _dir.IsTransitOfficeTenantAsync(Tenant, Arg.Any<CancellationToken>()).Returns(esOrganismo);

        (await new OtPermissionTenantRule(_dir).CheckAsync(Req(action, slug), Ct)).Should().Be(esperado);
    }

    [Theory]
    [InlineData(false, new[] { "logqx.read" })]
    [InlineData(true, new[] { "ot.tramites.manage", "logqx.read" })]
    public async Task El_catalogo_otorgable_del_Super_Admin_oculta_los_OT_en_una_compania(bool esOrganismo, string[] esperado)
    {
        _repo.ListEnforcedPermissionsAsync(Arg.Any<CancellationToken>()).Returns([OtTramites, LogQx]);
        _dir.IsTransitOfficeTenantAsync(Tenant, Arg.Any<CancellationToken>()).Returns(esOrganismo);
        var sut = new ManageUserPermissionOverridesHandler(
            _repo, new PermissionCapCalculator(_repo), new GrantModuleOverridesHandler(_repo), [], targets: _dir);

        var list = await sut.ListGrantableAsync(Tenant, new OverrideActor(SuperAdminId, true), Ct);

        list.Select(p => p.Slug).Should().BeEquivalentTo(esperado);
    }
}
