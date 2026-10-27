using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>Hallazgo review Épica #12753: overrides sobre permisos cuyo flag de enforcement está apagado (falsa revocación).</summary>
public sealed class ManageOverridesEnforcementStateTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly PermissionCatalogEntry Apagado = new(Guid.NewGuid(), "reportes.read", "Reportes", "reportes", "tramites", "true");
    private static readonly PermissionCatalogEntry Apagado2 = new(Guid.NewGuid(), "reportes.uso.read", "Uso", "reportes", "tramites", "true");
    private static readonly PermissionCatalogEntry Exigido = new(Guid.NewGuid(), "ict.pii.reveal", "PII", "ict", "tramites", "true");
    private static readonly OverrideActor Admin = new(AdminId, false);

    private readonly IUserPermissionOverrideRepository _repo = Substitute.For<IUserPermissionOverrideRepository>();
    private readonly IPermissionEnforcementState _state = Substitute.For<IPermissionEnforcementState>();

    private ManageUserPermissionOverridesHandler Sut()
    {
        foreach (var p in new[] { Apagado, Apagado2, Exigido })
        {
            _repo.GetPermissionAsync(p.Id, Arg.Any<CancellationToken>()).Returns(p);
        }

        _repo.ListEnforcedPermissionsAsync(Arg.Any<CancellationToken>()).Returns([Apagado, Apagado2, Exigido]);
        _repo.ListExcludedPermissionIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        _repo.ListActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetRolePermissionIdsAsync(Tenant, AdminId, Arg.Any<CancellationToken>())
            .Returns(new HashSet<Guid> { Apagado.Id, Apagado2.Id, Exigido.Id });
        _repo.GetModuleActionsAsync("reportes", Arg.Any<CancellationToken>())
            .Returns(new ModuleActionsSnapshot("reportes", "tramites", [new(Apagado.Id, Apagado.Slug), new(Apagado2.Id, Apagado2.Slug)]));
        _state.IsEffectivelyEnforced(Arg.Any<string>()).Returns(true);
        _state.IsEffectivelyEnforced(Apagado.Slug).Returns(false);
        _state.IsEffectivelyEnforced(Apagado2.Slug).Returns(false);
        return new ManageUserPermissionOverridesHandler(
            _repo, new PermissionCapCalculator(_repo), new GrantModuleOverridesHandler(_repo), [], _state);
    }

    private static async Task<PermissionOverrideRejectedException> Rejected(Task t) =>
        (await FluentActions.Awaiting(() => t).Should().ThrowAsync<PermissionOverrideRejectedException>()).Which;

    [Theory]
    [InlineData("allow")]
    [InlineData("deny")]
    public async Task Allow_y_deny_sobre_un_permiso_con_flag_apagado_dan_NOT_ENFORCED_y_se_auditan(string effect)
    {
        var ex = await Rejected(Sut().SetAsync(Tenant, Admin, TargetId, Apagado.Id, effect, TestContext.Current.CancellationToken));

        ex.ReasonCode.Should().Be("NOT_ENFORCED");
        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Is<IReadOnlyList<UserPermissionOverride>>(l => l.Count == 0),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Outcome == "REJECTED" && l[0].ReasonCode == "NOT_ENFORCED" && l[0].PermissionSlug == "reportes.read"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_permiso_exigido_sigue_otorgandose()
    {
        var r = await Sut().SetAsync(Tenant, Admin, TargetId, Exigido.Id, "deny", TestContext.Current.CancellationToken);

        r.Changed.Should().BeTrue();
    }

    [Fact]
    public async Task Quitar_el_override_de_un_permiso_con_flag_apagado_siempre_se_puede()
    {
        var sut = Sut();
        var existing = new UserPermissionOverride(
            Guid.NewGuid(), Tenant, TargetId, "tramites", Apagado.Id, "deny", PermissionOverrideSources.Individual, null, AdminId);
        _repo.ListActiveAsync(Tenant, TargetId, Arg.Any<CancellationToken>()).Returns([existing]);

        (await sut.RemoveAsync(Tenant, Admin, TargetId, Apagado.Id, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task Modulo_completo_con_todo_apagado_rechaza_NOT_ENFORCED_auditado()
    {
        var ex = await Rejected(Sut().GrantModuleAsync(Tenant, Admin, TargetId, "reportes", "deny", TestContext.Current.CancellationToken));

        ex.ReasonCode.Should().Be("NOT_ENFORCED");
        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Outcome == "REJECTED" && l[0].ReasonCode == "NOT_ENFORCED" && l[0].ModuleCode == "reportes"),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().AddAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Any<IReadOnlyList<UserPermissionAuditEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modulo_completo_con_flag_encendido_se_otorga()
    {
        var sut = Sut();
        _state.IsEffectivelyEnforced(Arg.Any<string>()).Returns(true);

        var r = await sut.GrantModuleAsync(Tenant, Admin, TargetId, "reportes", "allow", TestContext.Current.CancellationToken);

        r.Created.Should().Be(2);
        r.NotEnforced.Should().Be(0);
    }

    [Fact]
    public async Task Modulo_completo_mixto_aplica_solo_lo_exigido()
    {
        var sut = Sut();
        _state.IsEffectivelyEnforced(Apagado2.Slug).Returns(true);

        var r = await sut.GrantModuleAsync(Tenant, Admin, TargetId, "reportes", "allow", TestContext.Current.CancellationToken);

        r.Created.Should().Be(1);
        r.NotEnforced.Should().Be(1);
    }

    [Fact]
    public async Task Grantable_excluye_los_permisos_con_flag_apagado()
    {
        var sut = Sut();

        var list = await sut.ListGrantableAsync(Tenant, Admin, TestContext.Current.CancellationToken);

        list.Select(p => p.Slug).Should().BeEquivalentTo(["ict.pii.reveal"]);
    }

    [Fact]
    public async Task Grantable_sin_estado_de_enforcement_ofrece_todo_lo_enforced()
    {
        var sut = Sut();
        _state.IsEffectivelyEnforced(Arg.Any<string>()).Returns(true);

        (await sut.ListGrantableAsync(Tenant, Admin, TestContext.Current.CancellationToken)).Should().HaveCount(3);
    }
}
