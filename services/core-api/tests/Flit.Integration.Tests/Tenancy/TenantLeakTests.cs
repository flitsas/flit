using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// HU #12322 — suite NEGATIVA de fuga entre clientes sobre el escenario <see cref="HierarchyScenario"/>
/// (P cabeza de grupo; C1 y C2 hijos; X ajeno; S aislado) y contra los repositorios reales en
/// PostgreSQL:
/// <list type="bullet">
///   <item>AC2 — un usuario de C1 no obtiene ninguna fila de C2 (hermanos).</item>
///   <item>AC3 — C1 tampoco obtiene filas de P (el hijo nunca lee a la red).</item>
///   <item>AC4 — ni C1 ni P obtienen filas de X (cliente ajeno a la red).</item>
///   <item>ADR-0057 — conjunto de lectura vacío / tenant inexistente ⇒ cero filas, nunca «todas».</item>
/// </list>
/// Cada consulta del inventario (<see cref="CoveredQueries.All"/>) se ejecuta como C1, P, X y S:
/// toda fila debe pertenecer al lector. La aserción (<see cref="LeakAssert"/>) nombra id y tenant de
/// cada fila ajena, y <see cref="El_helper_de_fuga_detecta_filas_ajenas"/> demuestra que dispara.
/// <para>
/// Uso de ejemplo: <c>await HierarchyScenario.SeedAsync(Fixture)</c>; luego
/// <c>CoveredQueries.RunAsync("Q02", ctx, HierarchyScenario.C1)</c> y
/// <c>LeakAssert.NoForeignRows(...)</c> con el conjunto permitido {C1}.
/// </para>
/// </summary>
public sealed class TenantLeakTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    public static IEnumerable<TheoryDataRow<string, string>> QueriesPorLector()
    {
        foreach (var query in CoveredQueries.All.Where(q => !q.TenantOnly))
        {
            // C2 no se itera como lector: es simétrico a C1 (misma semilla) y cada caso cuesta una
            // resiembra completa; C2 sí aparece como DUEÑO de las filas que C1 no debe ver (AC2).
            foreach (var reader in new[] { "C1", "P", "X", "S" })
                yield return new TheoryDataRow<string, string>(query.Id, reader);
        }
    }

    private static Guid Tenant(string code) => code switch
    {
        "P" => HierarchyScenario.P,
        "C1" => HierarchyScenario.C1,
        "C2" => HierarchyScenario.C2,
        "X" => HierarchyScenario.X,
        "S" => HierarchyScenario.S,
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    /// <summary>
    /// AC2 + AC3 + AC4 en una sola prueba por (consulta, lector): como C1 no hay filas de C2, P, X ni S;
    /// como P no hay filas de C1, C2, X ni S (la lectura ampliada de la cabeza de grupo aplica SOLO a
    /// rutas nuevas con <c>TenantScope</c>, ADR-0057, así que las consultas legadas siguen siendo propias).
    /// </summary>
    [PostgresTheory]
    [MemberData(nameof(QueriesPorLector))]
    public async Task Ninguna_consulta_devuelve_filas_de_otro_cliente(string queryId, string readerCode)
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var reader = Tenant(readerCode);
        var query = CoveredQueries.Get(queryId);
        var allowed = new HashSet<Guid> { reader };

        await using var ctx = NewContext();
        var outcome = await CoveredQueries.RunAsync(queryId, ctx, reader);

        if (outcome.RowIds is { } rows)
        {
            LeakAssert.NoForeignRows(query.ToString(), reader, allowed, rows, HierarchyScenario.OwnerOf, id => id);
            rows.Should().HaveCount(query.OwnRows,
                $"{query} como {readerCode} debe devolver exactamente sus {query.OwnRows} fila(s) propias");
        }
        else
        {
            var total = (long)query.OwnRows * HierarchyScenario.Clients.Count;
            LeakAssert.OwnAggregateOnly(query.ToString(), reader, outcome.Scalar!.Value, query.OwnRows, total);
        }
    }

    /// <summary>
    /// AC2 explícito: dos hermanos con trámites; el detalle por id de C1 con cada trámite de C2 es
    /// <c>null</c> (no 403 ni fila), y viceversa.
    /// </summary>
    [PostgresFact]
    public async Task Hermanos_no_ven_el_detalle_del_otro()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);

        foreach (var ajeno in HierarchyScenario.ProceduresOf(HierarchyScenario.C2))
            (await repo.GetByIdWithDetailsAsync(ajeno, HierarchyScenario.C1, CancellationToken.None)).Should().BeNull(
                $"C1 no debe ver el trámite {ajeno} de C2 (AC2)");

        foreach (var propio in HierarchyScenario.ProceduresOf(HierarchyScenario.C1))
            (await repo.GetByIdWithDetailsAsync(propio, HierarchyScenario.C1, CancellationToken.None)).Should().NotBeNull();
    }

    /// <summary>
    /// Demostración de que la suite no está en verde por vacuidad: <c>WhereTenantInScope</c> con
    /// <c>TenantScope.All</c> devuelve filas de los cinco clientes, y el helper de fuga las nombra
    /// (id + tenant) al afirmar como C1.
    /// </summary>
    [PostgresFact]
    public async Task El_helper_de_fuga_detecta_filas_ajenas()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var todas = await ctx.ProcedureInstances.AsNoTracking()
            .WhereTenantInScope(TenantScope.All(), p => p.TenantId)
            .Select(p => new { p.Id, p.TenantId })
            .ToListAsync();

        todas.Should().HaveCount(10);

        var act = () => LeakAssert.NoForeignRows(
            "Q27 (demostración)", HierarchyScenario.C1, new HashSet<Guid> { HierarchyScenario.C1 },
            todas, r => r.TenantId, r => r.Id);

        var ex = act.Should().Throw<LeakDetectedException>().Which;
        ex.Message.Should().Contain("FUGA en Q27")
            .And.Contain("8 fila(s) ajena(s)")
            .And.Contain($"del tenant C2 ({HierarchyScenario.C2})")
            .And.Contain($"del tenant P ({HierarchyScenario.P})")
            .And.Contain($"del tenant X ({HierarchyScenario.X})")
            .And.Contain(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2).ToString());
    }

    // ── Q27: ruta nueva con TenantScope (ADR-0057) ──────────────────────────────────────────────

    [PostgresFact]
    public async Task Q27_Single_del_hijo_solo_lee_lo_propio()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var rows = await ctx.ProcedureInstances.AsNoTracking()
            .WhereTenantInScope(TenantScope.Single(HierarchyScenario.C1), p => p.TenantId)
            .Select(p => p.Id)
            .ToListAsync();

        LeakAssert.NoForeignProcedures("Q27 Single(C1)", HierarchyScenario.C1, new HashSet<Guid> { HierarchyScenario.C1 }, rows);
        rows.Should().BeEquivalentTo(HierarchyScenario.ProceduresOf(HierarchyScenario.C1));
    }

    [PostgresFact]
    public async Task Q27_Group_de_la_cabeza_lee_padre_e_hijos_pero_nunca_al_ajeno()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var scope = TenantScope.Group(HierarchyScenario.P, [HierarchyScenario.C1, HierarchyScenario.C2], GroupKind.Concesion);

        var rows = await ctx.ProcedureInstances.AsNoTracking()
            .WhereTenantInScope(scope, p => p.TenantId)
            .Select(p => p.Id)
            .ToListAsync();

        LeakAssert.NoForeignProcedures("Q27 Group(P)", HierarchyScenario.P, scope.ReadTenantIds, rows);
        rows.Should().HaveCount(6).And.NotContain(HierarchyScenario.ProceduresOf(HierarchyScenario.X))
            .And.NotContain(HierarchyScenario.ProceduresOf(HierarchyScenario.S));
    }

    /// <summary>ADR-0057: un tenant que no existe resuelve <c>Single</c> (fail-closed) y lee CERO filas, nunca todas.</summary>
    [PostgresFact]
    public async Task Q27_tenant_inexistente_lee_cero_filas_nunca_todas()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var inexistente = new Guid("a0000000-0000-4000-8000-00000000dead");

        var scope = await NewResolver(ctx).ResolveAsync(inexistente);
        scope.IsAll.Should().BeFalse();
        scope.ReadTenantIds.Should().Equal(inexistente);

        var rows = await ctx.ProcedureInstances.AsNoTracking()
            .WhereTenantInScope(scope, p => p.TenantId)
            .ToListAsync();

        rows.Should().BeEmpty("un conjunto de lectura sin datos es cero filas, no ausencia de filtro");
    }

    // ── Q26: DbTenantScopeResolver contra la base real ───────────────────────────────────────────

    [PostgresFact]
    public async Task Q26_hijo_resuelve_Single_propio()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.C1);

        scope.IsGroup.Should().BeFalse();
        scope.WriteTenantId.Should().Be(HierarchyScenario.C1);
        scope.ReadTenantIds.Should().Equal(HierarchyScenario.C1);
        scope.CanRead(HierarchyScenario.P).Should().BeFalse("el hijo nunca lee al padre (AC3)");
        scope.CanRead(HierarchyScenario.C2).Should().BeFalse("el hijo nunca lee a su hermano (AC2)");
    }

    [PostgresFact]
    public async Task Q26_cabeza_con_interruptor_encendido_resuelve_Group_con_sus_hijos()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);

        scope.IsGroup.Should().BeTrue();
        scope.WriteTenantId.Should().Be(HierarchyScenario.P);
        scope.ReadTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);
        scope.CanRead(HierarchyScenario.X).Should().BeFalse("la red nunca lee a un cliente ajeno (AC4)");
        scope.CanWrite(HierarchyScenario.C1).Should().BeFalse("leer al hijo no otorga escritura");
    }

    [PostgresFact]
    public async Task Q26_cabeza_con_interruptor_apagado_resuelve_Single()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var admin = NewContext())
        {
            await new DbHierarchySwitches(admin, NullLogger<DbHierarchySwitches>.Instance)
                .SetAsync(IHierarchySwitches.GroupReadScopeKey, isEnabled: false, actorUserId: null);
        }

        await using var ctx = NewContext();
        var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);

        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().Equal(HierarchyScenario.P);
    }

    [PostgresFact]
    public async Task Q26_ajeno_e_inexistente_resuelven_Single_fail_closed()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var resolver = NewResolver(ctx);

        var ajeno = await resolver.ResolveAsync(HierarchyScenario.X);
        ajeno.IsGroup.Should().BeFalse();
        ajeno.ReadTenantIds.Should().Equal(HierarchyScenario.X);

        var inexistente = Guid.NewGuid();
        var scope = await resolver.ResolveAsync(inexistente);
        scope.IsAll.Should().BeFalse();
        scope.IsGroup.Should().BeFalse();
        scope.ReadTenantIds.Should().Equal(inexistente);
    }

    // ── Q24 / Q25: organismo de tránsito por grants ─────────────────────────────────────────────

    /// <summary>
    /// HU #12350 AC7 — la bandeja lista trámites ya recibidos por el organismo (todos los clientes
    /// del escenario entregaron a Ot1), no la vigencia del grant.
    /// </summary>
    [PostgresFact]
    public async Task Q24_bandeja_del_OT_lista_tramites_recibidos_independiente_del_grant()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher());

        var page = await repo.ListAsync(HierarchyScenario.O, new OtClientProcedureFilter { Page = 1, PageSize = 100 });

        var expectedIds = HierarchyScenario.Clients
            .Select(HierarchyScenario.DeliveredProcedureOf)
            .ToHashSet();

        LeakAssert.NoForeignRows("Q24 OtClientProcedureRepository.ListAsync", HierarchyScenario.O,
            expectedIds, page.Items, r => r.Id, r => r.Id);
        page.Items.Should().HaveCount(HierarchyScenario.Clients.Count);
    }

    [PostgresFact]
    public async Task Q25_companias_cliente_del_OT_solo_las_del_grant()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();

        var options = await new OtMetricsReadRepository(ctx).ListClientCompaniesAsync(HierarchyScenario.O);

        options.Should().NotBeNull();
        LeakAssert.NoForeignRows("Q25 OtMetricsReadRepository.ListClientCompaniesAsync", HierarchyScenario.O,
            new HashSet<Guid> { HierarchyScenario.C1 }, options!, r => r.TenantId, r => r.TenantId);
        options.Should().ContainSingle().Which.TenantId.Should().Be(HierarchyScenario.C1);
    }

    /// <summary>Q13 no devuelve filas con tenant: se comprueba el contenido exacto (C1 → Ot1; el resto → Ot1 también, pero cada uno una sola).</summary>
    [PostgresFact]
    public async Task Q13_convenios_de_OT_son_exactamente_los_propios()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new CompanyAgreementRepository(ctx);

        foreach (var tenant in HierarchyScenario.Clients)
            (await repo.ListActiveOfficeIdsAsync(tenant)).Should().Equal([HierarchyScenario.Ot1],
                $"{HierarchyScenario.CodeOf(tenant)} tiene un único convenio propio");
    }

    private static DbTenantScopeResolver NewResolver(Infrastructure.Persistence.FlitDbContext ctx) =>
        new(ctx, new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance), NullLogger<DbTenantScopeResolver>.Instance);
}
