using Flit.Analytics.Application.Dtos;
using Flit.Analytics.Application.Queries;
using Flit.Analytics.Application.Queries.Network;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12359 (Feature #12257, épica #12235) — estadísticas de red contra PostgreSQL real sobre
/// <see cref="HierarchyScenario"/> (P cabeza; C1/C2 hijos; X ajeno; S aislado) más trámites extra en
/// estados y fechas distintos (dentro y fuera del rango). Por CADA consulta ampliada (Q33 overview,
/// Q34 top de productividad, Q35 tendencia mensual — AC2) hay un caso con tres clientes:
/// <list type="bullet">
///   <item>(a) AC1 — Group(P,[C1,C2]) = suma exacta de lo que P, C1 y C2 obtienen por las rutas viejas;
///   nunca X ni S.</item>
///   <item>(b) <c>childTenantId = C1</c> ⇒ exactamente lo de C1.</item>
///   <item>(c) AC4 — conjunto vacío ⇒ cero filas SIN ir a la base (y en la base, <c>= ANY('{}')</c> ⇒ 0).</item>
///   <item>(d) AC6 — S por las rutas viejas (<c>Guid?</c>) obtiene lo mismo que antes; la vista global
///   (<c>null</c>) sigue siendo «todas las compañías».</item>
/// </list>
/// Uso de ejemplo: <c>await new AnalyticsNetworkReadRepository(ctx).GetNetworkOverviewAsync(GroupP().ReadTenantIds, From, To)</c>.
/// </summary>
public sealed class NetworkAnalyticsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly DateOnly From = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));
    private static readonly DateOnly To = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
    private static readonly IReadOnlySet<Guid> Nadie = new HashSet<Guid>();

    private static TenantScope GroupP() =>
        TenantScope.Group(HierarchyScenario.P, [HierarchyScenario.C1, HierarchyScenario.C2], GroupKind.Concesion);

    // ── Q33 — overview ────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Q33_Overview_a_Group_es_la_suma_de_P_C1_y_C2_y_nunca_X_ni_S()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var red = new AnalyticsNetworkReadRepository(ctx);
        var viejo = new AnalyticsReadRepository(ctx);

        var grupo = await red.GetNetworkOverviewAsync(GroupP().ReadTenantIds, From, To);
        var p = await viejo.GetOverviewAsync(HierarchyScenario.P, From, To);
        var c1 = await viejo.GetOverviewAsync(HierarchyScenario.C1, From, To);
        var c2 = await viejo.GetOverviewAsync(HierarchyScenario.C2, From, To);
        var global = await viejo.GetOverviewAsync(null, From, To);

        // Suma exacta por (categoría, estado) del padre y sus dos hijos.
        Flatten(grupo.Items).Should().BeEquivalentTo(SumFlat(p, c1, c2));
        grupo.Items.Sum(c => c.Total).Should().Be(2 + 3 + 2, "P: 2; C1: 2 + rechazado en rango; C2: 2 (el suyo extra está fuera de rango)");
        grupo.Items.SelectMany(c => c.ByStatus).Single(s => s.Status == TramiteEstado.Rechazado).Count.Should().Be(1);
        grupo.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        // Y no es el total del escenario: X y S quedan fuera (fuga = coincidir con el global).
        LeakAssert.OwnAggregateOnly("Q33 network overview Group(P)", HierarchyScenario.P, grupo.Items.Sum(c => c.Total), 7, global.Sum(c => c.Total));
        global.Sum(c => c.Total).Should().Be(7 + 2 + 2 + 1, "X y S siguen en la vista global, con el otro extra de X");
    }

    [PostgresFact]
    public async Task Q33_Overview_b_childTenantId_C1_es_exactamente_lo_de_C1()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var (tenantIds, error) = NetworkAnalyticsScope.Resolve(GroupP(), HierarchyScenario.C1, From, To);
        error.Should().BeNull();

        var soloC1 = await new AnalyticsNetworkReadRepository(ctx).GetNetworkOverviewAsync(tenantIds!, From, To);
        var viejoC1 = await new AnalyticsReadRepository(ctx).GetOverviewAsync(HierarchyScenario.C1, From, To);

        Flatten(soloC1.Items).Should().BeEquivalentTo(Flatten(viejoC1));
        soloC1.Items.Sum(c => c.Total).Should().Be(3);
        soloC1.ReachedTenantIds.Should().Equal(HierarchyScenario.C1);

        // Hijo ajeno: rechazado en memoria, el conjunto no llega a existir.
        var (ajeno, errorAjeno) = NetworkAnalyticsScope.Resolve(GroupP(), HierarchyScenario.X, From, To);
        ajeno.Should().BeNull();
        errorAjeno.Should().Be(NetworkScopePolicy.ChildOutOfScope);
    }

    [PostgresFact]
    public async Task Q33_Overview_c_conjunto_vacio_devuelve_cero_filas_sin_ir_a_la_base()
    {
        await SeedWithExtrasAsync();

        // Sin base alcanzable: si el repositorio intentara abrir la conexión, fallaría.
        await using var sinBase = UnreachableContext();
        var vacio = await new AnalyticsNetworkReadRepository(sinBase).GetNetworkOverviewAsync(Nadie, From, To);
        vacio.Items.Should().BeEmpty();
        vacio.ReachedTenantIds.Should().BeEmpty();

        // Y si el arreglo vacío llegara al motor, tampoco sería «sin filtro»: = ANY('{}'::uuid[]) ⇒ 0 filas.
        (await CountWithTenantsArrayAsync([])).Should().Be(0);
        (await CountWithTenantsArrayAsync([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2])).Should().Be(7);
    }

    [PostgresFact]
    public async Task Q33_Overview_d_S_por_las_rutas_viejas_obtiene_lo_mismo_que_antes()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var viejo = new AnalyticsReadRepository(ctx);

        var (viejoS, errorViejo) = await new GetAnalyticsOverviewHandler(viejo)
            .HandleAsync(new GetAnalyticsOverviewQuery(HierarchyScenario.S, From, To));
        var nuevoS = await new AnalyticsNetworkReadRepository(ctx).GetNetworkOverviewAsync(new HashSet<Guid> { HierarchyScenario.S }, From, To);

        errorViejo.Should().BeNull();
        viejoS!.TenantId.Should().Be(HierarchyScenario.S);
        viejoS.Categories.Sum(c => c.Total).Should().Be(2, "S sigue teniendo sus dos trámites de la semilla simétrica");
        Flatten(viejoS.Categories).Should().BeEquivalentTo(Flatten(nuevoS.Items), "la consulta nueva sobre {S} coincide con la vieja");
        LeakAssert.OwnAggregateOnly("Q16 overview S (ruta vieja)", HierarchyScenario.S, viejoS.Categories.Sum(c => c.Total), 2, 12);
    }

    // ── Q34 — top de productividad ────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Q34_Top_a_Group_reune_a_los_radicadores_de_P_C1_y_C2_y_nunca_a_los_de_X_ni_S()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var red = new AnalyticsNetworkReadRepository(ctx);
        var viejo = new AnalyticsReadRepository(ctx);

        var grupo = await red.GetNetworkTopProducersAsync(GroupP().ReadTenantIds, From, To, 100);
        var propios = new List<TopProducerDto>();
        foreach (var t in new[] { HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2 })
            propios.AddRange(await viejo.GetTopProducersAsync(t, From, To, 100));
        var global = await viejo.GetTopProducersAsync(null, From, To, 100);

        grupo.Items.Should().BeEquivalentTo(propios, "cada gestor radica solo en su cliente: la unión es la suma");
        grupo.Items.Select(i => i.UserId).Should().BeEquivalentTo(
            [HierarchyScenario.UserOf(HierarchyScenario.P), HierarchyScenario.UserOf(HierarchyScenario.C1), HierarchyScenario.UserOf(HierarchyScenario.C2)]);
        grupo.Items.Single(i => i.UserId == HierarchyScenario.UserOf(HierarchyScenario.C1)).RejectedCount.Should().Be(1, "el rechazado extra de C1 está en rango");
        grupo.Items.Should().NotContain(i => i.UserId == HierarchyScenario.UserOf(HierarchyScenario.X))
            .And.NotContain(i => i.UserId == HierarchyScenario.UserOf(HierarchyScenario.S));
        grupo.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        global.Should().HaveCount(5, "la vista global sigue siendo todas las compañías");
        // El límite se respeta sobre la red completa (ranking único), no por cliente.
        (await red.GetNetworkTopProducersAsync(GroupP().ReadTenantIds, From, To, 2)).Items.Should().HaveCount(2);
    }

    [PostgresFact]
    public async Task Q34_Top_b_childTenantId_C1_es_exactamente_el_radicador_de_C1()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var (tenantIds, _) = NetworkAnalyticsScope.Resolve(GroupP(), HierarchyScenario.C1, From, To);

        var soloC1 = await new AnalyticsNetworkReadRepository(ctx).GetNetworkTopProducersAsync(tenantIds!, From, To, 100);
        var viejoC1 = await new AnalyticsReadRepository(ctx).GetTopProducersAsync(HierarchyScenario.C1, From, To, 100);

        soloC1.Items.Should().BeEquivalentTo(viejoC1);
        soloC1.Items.Should().ContainSingle().Which.UserId.Should().Be(HierarchyScenario.UserOf(HierarchyScenario.C1));
        soloC1.ReachedTenantIds.Should().Equal(HierarchyScenario.C1);
    }

    [PostgresFact]
    public async Task Q34_Top_c_conjunto_vacio_devuelve_cero_filas_sin_ir_a_la_base()
    {
        await SeedWithExtrasAsync();
        await using var sinBase = UnreachableContext();

        var vacio = await new AnalyticsNetworkReadRepository(sinBase).GetNetworkTopProducersAsync(Nadie, From, To, 100);

        vacio.Items.Should().BeEmpty();
        vacio.ReachedTenantIds.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task Q34_Top_d_S_por_las_rutas_viejas_obtiene_lo_mismo_que_antes()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();

        var (viejoS, error) = await new GetTopProducersHandler(new AnalyticsReadRepository(ctx))
            .HandleAsync(new GetTopProducersQuery(HierarchyScenario.S, From, To, 5));
        var nuevoS = await new AnalyticsNetworkReadRepository(ctx).GetNetworkTopProducersAsync(new HashSet<Guid> { HierarchyScenario.S }, From, To, 5);

        error.Should().BeNull();
        viejoS.Should().ContainSingle().Which.Should().BeEquivalentTo(new TopProducerDto(HierarchyScenario.UserOf(HierarchyScenario.S), "Gestor S", 1, 0, 0));
        nuevoS.Items.Should().BeEquivalentTo(viejoS);
    }

    // ── Q35 — tendencia mensual ───────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Q35_MonthlyTrend_a_Group_suma_P_C1_y_C2_en_un_punto_por_mes_y_categoria()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var red = new AnalyticsNetworkReadRepository(ctx);
        var viejo = new AnalyticsReadRepository(ctx);

        var grupo = await red.GetNetworkMonthlyTrendAsync(GroupP().ReadTenantIds, From, To);
        var p = await viejo.GetMonthlyTrendAsync(HierarchyScenario.P, From, To);
        var c1 = await viejo.GetMonthlyTrendAsync(HierarchyScenario.C1, From, To);
        var c2 = await viejo.GetMonthlyTrendAsync(HierarchyScenario.C2, From, To);
        var global = await viejo.GetMonthlyTrendAsync(null, From, To);

        var esperado = p.Concat(c1).Concat(c2)
            .GroupBy(x => (x.Year, x.Month, x.Category))
            .Select(g => new MonthlyTrendPointDto(g.Key.Year, g.Key.Month, g.Key.Category, g.Sum(x => x.Total)))
            .ToList();
        grupo.Items.Should().BeEquivalentTo(esperado);
        grupo.Items.Select(i => (i.Year, i.Month, i.Category)).Should().OnlyHaveUniqueItems("un punto por año/mes/categoría, no uno por cliente");
        grupo.Items.Sum(i => i.Total).Should().Be(7);
        grupo.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        LeakAssert.OwnAggregateOnly("Q35 network monthly-trend Group(P)", HierarchyScenario.P, grupo.Items.Sum(i => i.Total), 7, global.Sum(i => i.Total));
    }

    [PostgresFact]
    public async Task Q35_MonthlyTrend_b_childTenantId_C1_es_exactamente_lo_de_C1()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();
        var (tenantIds, _) = NetworkAnalyticsScope.Resolve(GroupP(), HierarchyScenario.C1, From, To);

        var soloC1 = await new AnalyticsNetworkReadRepository(ctx).GetNetworkMonthlyTrendAsync(tenantIds!, From, To);
        var viejoC1 = await new AnalyticsReadRepository(ctx).GetMonthlyTrendAsync(HierarchyScenario.C1, From, To);

        soloC1.Items.Should().BeEquivalentTo(viejoC1);
        soloC1.Items.Sum(i => i.Total).Should().Be(3);
        soloC1.ReachedTenantIds.Should().Equal(HierarchyScenario.C1);
    }

    [PostgresFact]
    public async Task Q35_MonthlyTrend_c_conjunto_vacio_devuelve_cero_filas_sin_ir_a_la_base()
    {
        await SeedWithExtrasAsync();
        await using var sinBase = UnreachableContext();

        var vacio = await new AnalyticsNetworkReadRepository(sinBase).GetNetworkMonthlyTrendAsync(Nadie, From, To);

        vacio.Items.Should().BeEmpty();
        vacio.ReachedTenantIds.Should().BeEmpty();
    }

    [PostgresFact]
    public async Task Q35_MonthlyTrend_d_S_por_las_rutas_viejas_obtiene_lo_mismo_que_antes()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();

        var (viejoS, error) = await new GetMonthlyTrendHandler(new AnalyticsReadRepository(ctx))
            .HandleAsync(new GetMonthlyTrendQuery(HierarchyScenario.S, From, To));
        var nuevoS = await new AnalyticsNetworkReadRepository(ctx).GetNetworkMonthlyTrendAsync(new HashSet<Guid> { HierarchyScenario.S }, From, To);

        error.Should().BeNull();
        viejoS!.Sum(i => i.Total).Should().Be(2);
        nuevoS.Items.Should().BeEquivalentTo(viejoS);
    }

    // ── AC1 (handler completo) — el agregado de la red por el handler, con auditoría de alcanzados ──

    [PostgresFact]
    public async Task AC1_Handler_de_overview_entrega_la_suma_de_la_red_y_los_hijos_alcanzados()
    {
        await SeedWithExtrasAsync();
        await using var ctx = NewContext();

        var (result, error) = await new GetNetworkAnalyticsOverviewHandler(new AnalyticsNetworkReadRepository(ctx))
            .HandleAsync(new GetNetworkAnalyticsOverviewQuery(GroupP(), null, From, To));

        error.Should().BeNull();
        result!.Response.TenantId.Should().Be(HierarchyScenario.P);
        result.Response.Categories.Sum(c => c.Total).Should().Be(7);
        result.Response.Scope.TenantIds.Should().BeEquivalentTo(GroupP().ReadTenantIds);
        result.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Semilla canónica + extras que distinguen estado y fecha: C1 un trámite RECHAZADO dentro del rango
    /// (con su historial), C2 un trámite fuera del rango (hace 40 días) y X un trámite extra en rango
    /// (para que el global cambie y una fuga se note).
    /// </summary>
    private async Task SeedWithExtrasAsync()
    {
        var type = await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var now = DateTimeOffset.UtcNow;

        var rechazadoC1 = Extra(HierarchyScenario.C1, type.Id, TramiteEstado.Rechazado, now.AddDays(-3), "990001", "C1R");
        var viejoC2 = Extra(HierarchyScenario.C2, type.Id, TramiteEstado.Entregado, now.AddDays(-40), "990002", "C2V");
        var extraX = Extra(HierarchyScenario.X, type.Id, TramiteEstado.Borrador, now.AddDays(-2), "990003", "XXE");
        ctx.ProcedureInstances.AddRange(rechazadoC1, viejoC2, extraX);
        await ctx.SaveChangesAsync();

        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = HierarchyScenario.C1,
            ProcedureInstanceId = rechazadoC1.Id,
            FromStatus = TramiteEstado.Entregado,
            ToStatus = TramiteEstado.Rechazado,
            ChangedAt = now.AddDays(-2),
            ChangedBy = HierarchyScenario.UserOf(HierarchyScenario.C1),
        });
        await ctx.SaveChangesAsync();
    }

    private static ProcedureInstance Extra(Guid tenant, Guid typeId, string status, DateTimeOffset createdAt, string reference, string tag) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant,
        ProcedureTypeId = typeId,
        ReferenceNumber = reference,
        Status = status,
        Plate = $"{tag}00",
        CreatedByUserId = HierarchyScenario.UserOf(tenant),
        CreatedAt = createdAt,
    };

    private static List<(string Category, string Status, int Count)> Flatten(IEnumerable<CategoryMetricsDto> categories) =>
        categories.SelectMany(c => c.ByStatus.Select(s => (c.Category, s.Status, s.Count))).ToList();

    private static List<(string Category, string Status, int Count)> SumFlat(params IReadOnlyList<CategoryMetricsDto>[] sets) =>
        sets.SelectMany(Flatten)
            .GroupBy(x => (x.Category, x.Status))
            .Select(g => (g.Key.Category, g.Key.Status, g.Sum(x => x.Count)))
            .ToList();

    /// <summary>Contexto cuyo servidor no existe: cualquier consulta lanza al abrir la conexión.</summary>
    private static FlitDbContext UnreachableContext()
    {
        var options = new DbContextOptionsBuilder<FlitDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=nadie;Username=nadie;Password=nadie;Timeout=1")
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new FlitDbContext(options);
    }

    /// <summary>La misma cláusula del repositorio, ejecutada a mano con un <c>uuid[]</c> dado.</summary>
    private async Task<long> CountWithTenantsArrayAsync(Guid[] tenants)
    {
        await using var ctx = NewContext();
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM tramites.procedure_instances pi WHERE pi.tenant_id = ANY(@tenants) AND pi.deleted_at IS NULL AND pi.created_at::date BETWEEN @from AND @to", conn);
        cmd.Parameters.Add(new NpgsqlParameter("tenants", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tenants });
        cmd.Parameters.AddWithValue("from", From);
        cmd.Parameters.AddWithValue("to", To);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
