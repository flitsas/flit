using Flit.Modules.Security.Application.PermissionOverrides;
using Flit.Modules.Security.Application.PermissionOverrides.GrantModule;
using Flit.Modules.Security.Domain.PermissionOverrides;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.PermissionOverrides;

/// <summary>HU #13645: una prueba por regla (AC1 a AC7) y su integracion con el handler (lectura, quitar, modulo).</summary>
public sealed class PermissionOverrideRulesTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IOverrideTargetDirectory _dir = Substitute.For<IOverrideTargetDirectory>();
    private readonly IUserPermissionOverrideRepository _repo = Substitute.For<IUserPermissionOverrideRepository>();

    private static OverrideTargetInfo Info(
        bool member = true, string status = "active", bool pendingInvitation = false, Guid? home = null,
        bool superAdmin = false, bool exists = true, bool tenantExists = true, params string[] roles) =>
        new(tenantExists, exists, member, home, status, pendingInvitation, roles.ToHashSet(), superAdmin);

    private void Target(OverrideTargetInfo info) => _dir.GetAsync(Tenant, TargetId, Arg.Any<CancellationToken>()).Returns(info);

    private void Actor(OverrideTargetInfo info) => _dir.GetAsync(Tenant, ActorId, Arg.Any<CancellationToken>()).Returns(info);

    private static PermissionOverrideRequest Req(bool actorIsSuper = false, string action = "GRANT", Guid? target = null, string? enforced = "true") =>
        new(Tenant, new OverrideActor(ActorId, actorIsSuper), target ?? TargetId, action, "x.slug", enforced);

    private OverrideTargetLookup Lookup() => new(_dir);

    [Theory]
    [InlineData("AdminCompany")]
    [InlineData("ot_admin")]
    [InlineData("admin_tramites")]
    public async Task AC1_admin_no_super_no_modifica_a_otro_administrador(string role)
    {
        Target(Info(roles: role));
        (await new TargetIsAdminRule(Lookup()).CheckAsync(Req(), Ct)).Should().Be("TARGET_IS_ADMIN");
    }

    // HU #13655: el Gestor OT no es administrador, así que el Admin OT de su organismo sí lo configura.
    [Fact]
    public async Task HU13655_el_Gestor_OT_no_es_administrador_y_el_Admin_OT_lo_puede_editar()
    {
        Target(Info(roles: "gestor_tramites_ot"));
        (await new TargetIsAdminRule(Lookup()).CheckAsync(Req(), Ct)).Should().BeNull();
        AdministratorRoles.Any(["gestor_tramites_ot"]).Should().BeFalse();
    }

    [Fact]
    public async Task AC1_el_super_admin_si_puede_y_un_usuario_normal_pasa()
    {
        Target(Info(roles: "AdminCompany"));
        (await new TargetIsAdminRule(Lookup()).CheckAsync(Req(actorIsSuper: true), Ct)).Should().BeNull();
        Target(Info(roles: "Radicador"));
        (await new TargetIsAdminRule(Lookup()).CheckAsync(Req(), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task AC2_sobre_si_mismo_es_SELF_para_cualquier_administrador()
    {
        var rule = new SelfEditRule();
        (await rule.CheckAsync(Req(target: ActorId), Ct)).Should().Be("SELF");
        (await rule.CheckAsync(Req(actorIsSuper: true, target: ActorId), Ct)).Should().Be("SELF");
        (await rule.CheckAsync(Req(), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task AC3_destino_super_admin_es_TARGET_SUPERADMIN_incluso_para_el_super_admin()
    {
        Target(Info(superAdmin: true));
        var rule = new TargetSuperAdminRule(Lookup());
        (await rule.CheckAsync(Req(), Ct)).Should().Be("TARGET_SUPERADMIN");
        (await rule.CheckAsync(Req(actorIsSuper: true), Ct)).Should().Be("TARGET_SUPERADMIN");
    }

    [Theory]
    [InlineData("excluded", "EXCLUDED")]
    [InlineData("true", null)]
    [InlineData("false", null)]
    [InlineData(null, null)]
    public async Task AC4_solo_los_excluded_se_rechazan_como_EXCLUDED(string? enforced, string? expected) =>
        (await new ExcludedPermissionRule().CheckAsync(Req(enforced: enforced), Ct)).Should().Be(expected);

    [Fact]
    public async Task AC5_usuario_de_otra_compania_o_hija_es_OUT_OF_SCOPE()
    {
        var rule = new TenantScopeRule(Lookup());
        Actor(Info());
        Target(Info(member: false));
        (await rule.CheckAsync(Req(), Ct)).Should().Be("OUT_OF_SCOPE");

        // Miembro por una asignacion, pero su casa es otro tenant (hija): fuera del alcance.
        Target(Info(home: Guid.NewGuid()));
        (await new TenantScopeRule(Lookup()).CheckAsync(Req(), Ct)).Should().Be("OUT_OF_SCOPE");
    }

    [Fact]
    public async Task AC5_el_Super_Admin_actua_sobre_miembros_del_tenant_declarado_sin_ser_miembro()
    {
        Target(Info(home: Tenant));
        (await new TenantScopeRule(Lookup()).CheckAsync(Req(actorIsSuper: true), Ct)).Should().BeNull();
        Target(Info(member: false));
        (await new TenantScopeRule(Lookup()).CheckAsync(Req(actorIsSuper: true), Ct)).Should().Be("OUT_OF_SCOPE");
    }

    [Fact]
    public async Task AC7_un_actor_que_no_es_miembro_del_tenant_declarado_es_OUT_OF_SCOPE()
    {
        Target(Info());
        Actor(Info(member: false));
        (await new TenantScopeRule(Lookup()).CheckAsync(Req(), Ct)).Should().Be("OUT_OF_SCOPE");
        Actor(Info());
        (await new TenantScopeRule(Lookup()).CheckAsync(Req(), Ct)).Should().BeNull();
    }

    [Theory]
    [InlineData("pending", false)]
    [InlineData("active", true)]
    public async Task AC6_invitacion_pendiente_es_INVITATION_PENDING(string status, bool pendingInvitation)
    {
        Target(Info(status: status, pendingInvitation: pendingInvitation));
        (await new InvitationPendingRule(Lookup()).CheckAsync(Req(), Ct)).Should().Be("INVITATION_PENDING");
    }

    [Fact]
    public async Task AC6_usuario_activo_sin_invitacion_pasa()
    {
        Target(Info());
        (await new InvitationPendingRule(Lookup()).CheckAsync(Req(), Ct)).Should().BeNull();
    }

    [Fact]
    public async Task Usuario_inexistente_es_USER_NOT_FOUND_y_tenant_inexistente_lanza_sin_auditar()
    {
        Target(Info(exists: false));
        (await new UserExistsRule(Lookup()).CheckAsync(Req(), Ct)).Should().Be("USER_NOT_FOUND");
        Target(Info(exists: false, tenantExists: false));
        var ex = await FluentActions.Awaiting(() => new UserExistsRule(Lookup()).CheckAsync(Req(), Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();
        ex.Which.ReasonCode.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task La_lectura_solo_aplica_existencia_y_ambito()
    {
        Target(Info(superAdmin: true, status: "pending", pendingInvitation: true, roles: "AdminCompany"));
        var read = Req(action: PermissionOverrideActions.Read);
        (await new TargetSuperAdminRule(Lookup()).CheckAsync(read, Ct)).Should().BeNull();
        (await new TargetIsAdminRule(Lookup()).CheckAsync(read, Ct)).Should().BeNull();
        (await new InvitationPendingRule(Lookup()).CheckAsync(read, Ct)).Should().BeNull();
        (await new SelfEditRule().CheckAsync(Req(action: PermissionOverrideActions.Read, target: ActorId), Ct)).Should().BeNull();
    }

    // --- integracion con el handler -------------------------------------------------------------

    private ManageUserPermissionOverridesHandler Sut(bool withDirectory = false)
    {
        _repo.ListActiveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        _repo.ListExcludedPermissionIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid>());
        var lookup = Lookup();
        IPermissionOverrideRule[] rules =
        [
            new UserExistsRule(lookup), new SelfEditRule(), new TargetSuperAdminRule(lookup), new TenantScopeRule(lookup),
            new TargetIsAdminRule(lookup), new ExcludedPermissionRule(), new InvitationPendingRule(lookup),
        ];
        return new ManageUserPermissionOverridesHandler(
            _repo, new PermissionCapCalculator(_repo), new GrantModuleOverridesHandler(_repo), rules, targets: withDirectory ? _dir : null);
    }

    [Fact]
    public async Task Leer_los_overrides_de_un_usuario_de_otro_tenant_se_rechaza_y_se_audita_sin_tocar_el_listado()
    {
        Actor(Info());
        Target(Info(member: false));

        var ex = await FluentActions.Awaiting(() => Sut().ListAsync(Tenant, new OverrideActor(ActorId, false), TargetId, Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();

        ex.Which.ReasonCode.Should().Be("OUT_OF_SCOPE");
        await _repo.Received(1).ApplyAsync(Tenant, ActorId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].Action == "READ" && l[0].Outcome == "REJECTED" && l[0].ReasonCode == "OUT_OF_SCOPE"),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ListActiveAsync(Tenant, TargetId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU13647_el_rechazo_OUT_OF_SCOPE_se_audita_en_el_tenant_del_usuario_afectado_no_en_el_del_admin()
    {
        // Admin OT de Sabaneta (Tenant) lee y modifica al Radicador de otra compania (su casa es otro tenant).
        var targetHome = Guid.NewGuid();
        var permission = new PermissionCatalogEntry(Guid.NewGuid(), "tramites.create", "Crear", "tramites", "tramites", "true");
        _repo.GetPermissionAsync(permission.Id, Arg.Any<CancellationToken>()).Returns(permission);
        Actor(Info());
        Target(Info(member: false, home: targetHome));
        var actor = new OverrideActor(ActorId, false);

        await FluentActions.Awaiting(() => Sut(withDirectory: true).ListAsync(Tenant, actor, TargetId, Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();
        await FluentActions.Awaiting(() => Sut(withDirectory: true).SetAsync(Tenant, actor, TargetId, permission.Id, "allow", Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();

        await _repo.Received(1).ApplyAsync(targetHome, ActorId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l =>
                l[0].TenantId == targetHome && l[0].Action == "READ" && l[0].ReasonCode == "OUT_OF_SCOPE"),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).ApplyAsync(targetHome, ActorId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l =>
                l[0].TenantId == targetHome && l[0].Action == "GRANT" && l[0].ReasonCode == "OUT_OF_SCOPE"
                && l[0].PermissionSlug == "tramites.create"),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().ApplyAsync(Tenant, Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Any<IReadOnlyList<UserPermissionOverride>>(), Arg.Any<IReadOnlyList<UserPermissionAuditEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU13647_sin_home_conocido_el_OUT_OF_SCOPE_queda_en_el_tenant_en_el_que_se_opero()
    {
        Actor(Info());
        Target(Info(member: false));

        await FluentActions.Awaiting(() => Sut(withDirectory: true).ListAsync(Tenant, new OverrideActor(ActorId, false), TargetId, Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();

        await _repo.Received(1).ApplyAsync(Tenant, ActorId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].TenantId == Tenant && l[0].ReasonCode == "OUT_OF_SCOPE"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HU13647_los_demas_rechazos_siguen_en_el_tenant_en_el_que_se_opero()
    {
        // Destino del tenant (home = Tenant) que es administrador: TARGET_IS_ADMIN se audita donde se opero.
        var permission = new PermissionCatalogEntry(Guid.NewGuid(), "tramites.create", "Crear", "tramites", "tramites", "true");
        _repo.GetPermissionAsync(permission.Id, Arg.Any<CancellationToken>()).Returns(permission);
        Actor(Info());
        Target(Info(home: Tenant, roles: "AdminCompany"));

        var ex = await FluentActions.Awaiting(() =>
                Sut(withDirectory: true).SetAsync(Tenant, new OverrideActor(ActorId, false), TargetId, permission.Id, "deny", Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();

        ex.Which.ReasonCode.Should().Be("TARGET_IS_ADMIN");
        await _repo.Received(1).ApplyAsync(Tenant, ActorId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Is<IReadOnlyList<UserPermissionAuditEntry>>(l => l[0].TenantId == Tenant && l[0].ReasonCode == "TARGET_IS_ADMIN"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quitar_un_permiso_excluido_es_EXCLUDED_aunque_no_haya_override_activo()
    {
        var excluded = new PermissionCatalogEntry(Guid.NewGuid(), "usuarios.manage", "Usuarios", "usuarios", "tramites", "excluded");
        _repo.GetPermissionAsync(excluded.Id, Arg.Any<CancellationToken>()).Returns(excluded);
        Actor(Info());
        Target(Info());

        var ex = await FluentActions.Awaiting(() => Sut().RemoveAsync(Tenant, new OverrideActor(ActorId, false), TargetId, excluded.Id, Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();

        ex.Which.ReasonCode.Should().Be("EXCLUDED");
    }

    [Fact]
    public async Task Denegar_un_permiso_excluido_es_EXCLUDED_y_no_escribe_overrides()
    {
        var excluded = new PermissionCatalogEntry(Guid.NewGuid(), "rbac.manage", "Rbac", "rbac", "tramites", "excluded");
        _repo.GetPermissionAsync(excluded.Id, Arg.Any<CancellationToken>()).Returns(excluded);
        Actor(Info());
        Target(Info());

        var ex = await FluentActions.Awaiting(() => Sut().SetAsync(Tenant, new OverrideActor(ActorId, false), TargetId, excluded.Id, "deny", Ct))
            .Should().ThrowAsync<PermissionOverrideRejectedException>();

        ex.Which.ReasonCode.Should().Be("EXCLUDED");
        await _repo.DidNotReceive().ApplyAsync(Tenant, ActorId, Arg.Any<IReadOnlyList<Guid>>(),
            Arg.Is<IReadOnlyList<UserPermissionOverride>>(l => l.Count > 0), Arg.Any<IReadOnlyList<UserPermissionAuditEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Modulo_cuyas_acciones_son_todas_excluded_es_EXCLUDED_en_allow_y_en_deny()
    {
        var a = new ModuleActionRef(Guid.NewGuid(), "usuarios.manage");
        _repo.GetModuleActionsAsync("usuarios", Arg.Any<CancellationToken>()).Returns(new ModuleActionsSnapshot("usuarios", "tramites", [a]));
        _repo.ListEnforcedPermissionsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repo.GetRolePermissionIdsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { a.PermissionId });
        Actor(Info());
        Target(Info());

        foreach (var effect in new[] { "allow", "deny" })
        {
            var sut = Sut();
            _repo.ListExcludedPermissionIdsAsync(Arg.Any<CancellationToken>()).Returns(new HashSet<Guid> { a.PermissionId });
            var ex = await FluentActions.Awaiting(() => sut.GrantModuleAsync(Tenant, new OverrideActor(ActorId, false), TargetId, "usuarios", effect, Ct))
                .Should().ThrowAsync<PermissionOverrideRejectedException>();
            ex.Which.ReasonCode.Should().Be("EXCLUDED");
        }

        await _repo.DidNotReceive().AddAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<UserPermissionOverride>>(),
            Arg.Any<IReadOnlyList<UserPermissionAuditEntry>>(), Arg.Any<CancellationToken>());
    }
}
