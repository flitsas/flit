using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// HU #12652 (Feature #12257) — puerta de rol de la red contra PostgreSQL real: el alcance lo resuelve
/// <see cref="DbTenantScopeResolver"/> desde <c>companies.parent_tenant_id</c>/<c>is_group_parent</c>
/// (<see cref="HierarchyScenario"/>: P cabeza; C1/C2 hijas con <c>parent_tenant_id</c> puesto; S sin red)
/// y se encadena la MISMA secuencia del <c>GroupHeadReadFilter</c> (Validate(scope) ⇒ ValidateRole).
/// El proyecto no referencia <c>Flit.Api</c>: la capa HTTP la cubre <c>NetworkRoleGateTests</c> en
/// <c>Flit.Admin.Tests</c>; aquí se prueba que con la jerarquía REAL en base el hijo sigue en
/// <c>network_scope_required</c> y solo la cabeza sin AdminCompany cae en <c>network_role_required</c>.
/// Uso de ejemplo: <c>Gate(await resolver.ResolveAsync(HierarchyScenario.C1), ["AdminCompany"])</c> ⇒
/// <c>network_scope_required</c>; con <c>HierarchyScenario.P</c> y <c>["Radicador"]</c> ⇒ <c>network_role_required</c>.
/// </summary>
public sealed class NetworkRoleGateTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    /// <summary>Misma secuencia que <c>GroupHeadReadFilter.InvokeAsync</c>: alcance primero, rol después.</summary>
    private static string? Gate(TenantScope? scope, IEnumerable<string> roles) =>
        NetworkScopePolicy.Validate(scope) ?? NetworkScopePolicy.ValidateRole(roles);

    [PostgresFact]
    public async Task AC1_AC5_cabeza_real_con_AdminCompany_o_multi_rol_pasa()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);

        scope.IsGroup.Should().BeTrue();
        Gate(scope, ["AdminCompany"]).Should().BeNull();
        Gate(scope, ["Radicador", "AdminCompany"]).Should().BeNull("multi-rol: basta un AdminCompany");
    }

    [PostgresFact]
    public async Task AC2_cabeza_real_sin_AdminCompany_network_role_required()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);

        Gate(scope, ["Radicador"]).Should().Be(NetworkScopePolicy.RoleRequired);
        Gate(scope, ["Operador", "Gestor"]).Should().Be(NetworkScopePolicy.RoleRequired);
        Gate(scope, []).Should().Be(NetworkScopePolicy.RoleRequired);
    }

    [PostgresFact]
    public async Task AC6_hija_real_con_parent_tenant_id_network_scope_required_aunque_sea_AdminCompany()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var resolver = NewResolver(ctx);

        foreach (var hija in new[] { HierarchyScenario.C1, HierarchyScenario.C2 })
        {
            var scope = await resolver.ResolveAsync(hija);
            scope.IsGroup.Should().BeFalse($"{hija} tiene parent_tenant_id");
            Gate(scope, ["AdminCompany"]).Should().Be(NetworkScopePolicy.ScopeRequired, "la hija no tiene red: no llega a la puerta de rol");
            Gate(scope, ["Radicador"]).Should().Be(NetworkScopePolicy.ScopeRequired);
        }
    }

    [PostgresFact]
    public async Task AC6_cliente_sin_red_y_SuperAdmin_network_scope_required()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var sinRed = await NewResolver(ctx).ResolveAsync(HierarchyScenario.S);
        Gate(sinRed, ["AdminCompany"]).Should().Be(NetworkScopePolicy.ScopeRequired);

        // SuperAdmin: el middleware deja alcance total (nunca pasa por el resolver); sin alcance de grupo la puerta responde scope.
        Gate(null, ["SuperAdmin"]).Should().Be(NetworkScopePolicy.ScopeRequired, "ausencia de alcance no es alcance total");
    }

    private static DbTenantScopeResolver NewResolver(FlitDbContext ctx) =>
        new(ctx, new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance), NullLogger<DbTenantScopeResolver>.Instance);
}
