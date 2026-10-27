using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>HU #13644: tope del administrador (AC3, AC6, deny reduce el tope) y reglas de otorgar/quitar (AC4).</summary>
public sealed class ManageUserPermissionOverridesHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly PermissionCatalogEntry Reportes = new(Guid.NewGuid(), "reportes.read", "Reportes", "reportes", "tramites", "true");
    private static readonly PermissionCatalogEntry Pii = new(Guid.NewGuid(), "ict.pii.reveal", "PII", "ict", "tramites", "true");
    private static readonly PermissionCatalogEntry Sin = new(Guid.NewGuid(), "x.sin", "Sin", "x", "tramites", "false");
    private static readonly OverrideActor Admin = new(AdminId, false);

    private readonly IUserPermissionOverrideRepository _repo = Substitute.For<IUserPermissionOverrideRepository>();

    private ManageUserPermissionOverridesHandler Sut(params IPermissionOverrideRule[] rules)
    {
        foreach (var p in new[] { Reportes, Pii, Sin })
        {
            _repo.GetPermissionAsync(p.Id, Arg.Any<CancellationToken>()).Returns(p);
        }

        _repo.ListActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetRolePermissionIdsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        return new ManageUserPermissionOverridesHandler(_repo, new PermissionCapCalculator(_repo), new GrantModuleOverridesHandler(_repo), rules);
    }

    private void AdminHas(params Guid[] rolePermissionIds) =>
        _repo.GetRolePermissionIdsAsync(Tenant, AdminId, Arg.Any<CancellationToken>()).Returns(rolePermissionIds.ToHashSet());

    private void Active(Guid user, params UserPermissionOverride[] items) =>
        _repo.ListActiveAsync(Tenant, user, Arg.Any<CancellationToken>()).Returns(items);

    private static UserPermissionOverride Ov(Guid user, PermissionCatalogEntry p, string effect) =>
        new(Guid.NewGuid(), Tenant, user, p.ProductCode, p.Id, effect, PermissionOverrideSources.Individual, null, Guid.NewGuid());

    private static async Task<PermissionOverrideRejectedException> Rejected(Task t) =>
        (await FluentActions.Awaiting(() => t).Should().ThrowAsync<PermissionOverrideRejectedException>()).Which;

    [Fact]
    public async Task Otorga_dentro_del_tope_y_escribe_GRANT_con_before_y_after()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);

        var r = await sut.SetAsync(Tenant, Admin, TargetId, Reportes.Id, "allow", TestContext.Current.CancellationToken);

        r.Changed.Should().BeTrue();
        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 0),
            Arg.Is<IReadOnlyList<UserPermissionOverride>>(l => l.Count == 1 && l[0].Effect == "allow" && l[0].GrantedBy == AdminId),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Action == "GRANT" && l[0].Outcome == "OK" && l[0].ValueBefore == null && l[0].ValueAfter == "allow"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fuera_del_tope_rechaza_con_OUT_OF_CAP_y_audita_el_intento()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);

        var ex = await Rejected(sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "allow", TestContext.Current.CancellationToken));

        ex.ReasonCode.Should().Be("OUT_OF_CAP");
        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Is<IReadOnlyList<UserPermissionOverride>>(l => l.Count == 0),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Outcome == "REJECTED" && l[0].ReasonCode == "OUT_OF_CAP" && l[0].PermissionSlug == "ict.pii.reveal"),
            Arg.Any<CancellationToken>());
    }

    // AC3: un allow individual del admin no amplía su tope.
    [Fact]
    public async Task Los_allow_individuales_del_admin_no_amplian_el_tope()
    {
        var sut = Sut();
        AdminHas();
        Active(AdminId, Ov(AdminId, Reportes, "allow"));

        var ex = await Rejected(sut.SetAsync(Tenant, Admin, TargetId, Reportes.Id, "allow", TestContext.Current.CancellationToken));

        ex.ReasonCode.Should().Be("OUT_OF_CAP");
    }

    [Fact]
    public async Task Un_deny_individual_del_admin_reduce_su_tope()
    {
        var sut = Sut();
        AdminHas(Reportes.Id, Pii.Id);
        Active(AdminId, Ov(AdminId, Pii, "deny"));

        var cap = await new PermissionCapCalculator(_repo).GetCapAsync(Tenant, Admin, TestContext.Current.CancellationToken);

        cap.Contains(Reportes.Id).Should().BeTrue();
        cap.Contains(Pii.Id).Should().BeFalse();
        await Rejected(sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "allow", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task El_super_admin_no_tiene_tope_pero_solo_otorga_enforced_true()
    {
        var sut = Sut();
        var sa = new OverrideActor(AdminId, true);

        (await sut.SetAsync(Tenant, sa, TargetId, Pii.Id, "allow", TestContext.Current.CancellationToken, piiConfirmed: true)).Changed.Should().BeTrue();
        var ex = await Rejected(sut.SetAsync(Tenant, sa, TargetId, Sin.Id, "allow", TestContext.Current.CancellationToken));
        ex.ReasonCode.Should().Be("NOT_ENFORCED");
    }

    // AC4
    [Fact]
    public async Task Quitar_un_allow_fuera_del_tope_se_puede_pero_volver_a_habilitarlo_no()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);
        Active(TargetId, Ov(TargetId, Pii, "allow"));

        (await sut.RemoveAsync(Tenant, Admin, TargetId, Pii.Id, TestContext.Current.CancellationToken)).Should().BeTrue();
        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Is<IReadOnlyList<Guid>>(l => l.Count == 1), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Action == "REVOKE" && l[0].ValueBefore == "allow" && l[0].ValueAfter == null),
            Arg.Any<CancellationToken>());

        Active(TargetId);
        (await Rejected(sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "allow", TestContext.Current.CancellationToken))).ReasonCode.Should().Be("OUT_OF_CAP");
    }

    [Fact]
    public async Task Denegar_fuera_del_tope_se_puede()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);

        (await sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "deny", TestContext.Current.CancellationToken)).Changed.Should().BeTrue();
    }

    [Fact]
    public async Task Volver_al_rol_tras_un_deny_es_403_si_el_rol_lo_da_y_esta_fuera_del_tope()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);
        Active(TargetId, Ov(TargetId, Pii, "deny"));
        _repo.GetRolePermissionIdsAsync(Tenant, TargetId, Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { Pii.Id });

        (await Rejected(sut.RemoveAsync(Tenant, Admin, TargetId, Pii.Id, TestContext.Current.CancellationToken))).ReasonCode.Should().Be("OUT_OF_CAP");

        // Si el rol del usuario no lo da, quitar el deny no concede nada.
        _repo.GetRolePermissionIdsAsync(Tenant, TargetId, Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        (await sut.RemoveAsync(Tenant, Admin, TargetId, Pii.Id, TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    // AC6: sin cascada.
    [Fact]
    public async Task Perder_el_tope_despues_de_otorgar_no_toca_lo_otorgado()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);
        await sut.SetAsync(Tenant, Admin, TargetId, Reportes.Id, "allow", TestContext.Current.CancellationToken);
        _repo.ClearReceivedCalls();

        AdminHas(); // el admin pierde el permiso
        var cap = await new PermissionCapCalculator(_repo).GetCapAsync(Tenant, Admin, TestContext.Current.CancellationToken);

        cap.Contains(Reportes.Id).Should().BeFalse();
        await _repo.DidNotReceiveWithAnyArgs().ApplyAsync(default, default, default!, default!, default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Una_regla_de_extension_rechaza_antes_de_operar_y_queda_auditado()
    {
        var rule = Substitute.For<IPermissionOverrideRule>();
        rule.CheckAsync(Arg.Any<PermissionOverrideRequest>(), Arg.Any<CancellationToken>()).Returns("OUT_OF_SCOPE");
        var sut = Sut(rule);
        AdminHas(Reportes.Id);

        (await Rejected(sut.SetAsync(Tenant, Admin, TargetId, Reportes.Id, "allow", TestContext.Current.CancellationToken))).ReasonCode.Should().Be("OUT_OF_SCOPE");
        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Outcome == "REJECTED" && l[0].ReasonCode == "OUT_OF_SCOPE"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modulo_completo_solo_otorga_las_acciones_dentro_del_tope()
    {
        var sut = Sut();
        AdminHas(Reportes.Id);
        _repo.ListEnforcedPermissionsAsync(Arg.Any<CancellationToken>()).Returns([Reportes, Pii]);
        _repo.GetModuleActionsAsync("m", Arg.Any<CancellationToken>()).Returns(
            new ModuleActionsSnapshot("m", "tramites", [new ModuleActionRef(Reportes.Id, Reportes.Slug), new ModuleActionRef(Pii.Id, Pii.Slug)]));

        var r = await sut.GrantModuleAsync(Tenant, Admin, TargetId, "m", "allow", TestContext.Current.CancellationToken);

        // HU #13653: ict.pii.reveal no entra al módulo completo (exige confirmación Ley 1581 uno a uno).
        r.Created.Should().Be(1);
        r.OutOfCap.Should().Be(0);
        r.Excluded.Should().Be(1);
    }

    // HU #13653 (FB-17)
    [Fact]
    public async Task Habilitar_ict_pii_reveal_sin_confirmacion_lanza_y_no_escribe_nada()
    {
        var sut = Sut();
        AdminHas(Pii.Id);

        await FluentActions.Awaiting(() => sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "allow", TestContext.Current.CancellationToken))
            .Should().ThrowAsync<PiiConfirmationRequiredException>();

        await _repo.DidNotReceive().ApplyAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<IReadOnlyList<UserPermissionOverride>>(), Arg.Any<IReadOnlyList<UserPermissionAuditEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Habilitar_ict_pii_reveal_confirmado_escribe_GRANT_y_PII_REVEAL_CONFIRMED_en_la_misma_operacion()
    {
        var sut = Sut();
        AdminHas(Pii.Id);

        await sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "allow", TestContext.Current.CancellationToken, piiConfirmed: true);

        await _repo.Received(1).ApplyAsync(Tenant, AdminId, Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Is<IReadOnlyList<UserPermissionOverride>>(l => l.Count == 1),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l.Count == 2 && l[0].Action == "GRANT"
                && l[1].Action == "PII_REVEAL_CONFIRMED" && l[1].Outcome == "OK" && l[1].PermissionSlug == "ict.pii.reveal"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Denegar_ict_pii_reveal_no_exige_confirmacion()
    {
        var sut = Sut();

        (await sut.SetAsync(Tenant, Admin, TargetId, Pii.Id, "deny", TestContext.Current.CancellationToken)).Changed.Should().BeTrue();
    }

    // HU #13653: «Solo SA» = ningún administrador del tenant destino lo tiene en su tope.
    [Fact]
    public async Task Solo_SA_son_los_permisos_que_ningun_administrador_del_tenant_tiene_en_su_tope()
    {
        var sut = Sut();
        var other = Guid.NewGuid();
        _repo.ListTenantAdministratorIdsAsync(Tenant, Arg.Any<CancellationToken>()).Returns([AdminId, other]);
        AdminHas(Reportes.Id);

        var onlySa = await sut.ListSuperAdminOnlyAsync(Tenant, [Reportes, Pii], TestContext.Current.CancellationToken);

        onlySa.Should().BeEquivalentTo([Pii.Id]);
    }

    [Fact]
    public async Task Sin_administradores_en_el_tenant_todo_es_solo_SA()
    {
        var sut = Sut();
        _repo.ListTenantAdministratorIdsAsync(Tenant, Arg.Any<CancellationToken>()).Returns([]);

        var onlySa = await sut.ListSuperAdminOnlyAsync(Tenant, [Reportes, Pii], TestContext.Current.CancellationToken);

        onlySa.Should().BeEquivalentTo([Reportes.Id, Pii.Id]);
    }
}
