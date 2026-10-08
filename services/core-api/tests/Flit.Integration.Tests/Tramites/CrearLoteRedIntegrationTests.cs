using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13417 (épica #13216, ADR-0070 adenda v7) — lote desde la vista de red contra PostgreSQL real: el handler de
/// creación con el resolver real de <c>/tramites</c>, la consulta de red real (<c>WhereTenantInScope</c>) y el
/// repositorio real. Escenario <see cref="HierarchyScenario"/>: cabeza P con hijas C1 y C2, X ajena a la red; aquí se
/// añade una segunda cabeza Q con su hija Y (la «hija de otra cabeza» del riesgo 4 de §8).
/// <para>Uso de ejemplo:
/// <code>
/// var r = await handler.HandleAsync(command with { AlcanceRed = new LoteAlcanceRed(TenantScope.Group(P, [C1, C2], kind), null) }, ct);
/// </code></para>
/// </summary>
public sealed class CrearLoteRedIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid P = HierarchyScenario.P;
    private static readonly Guid C1 = HierarchyScenario.C1;
    private static readonly Guid C2 = HierarchyScenario.C2;
    private static readonly Guid X = HierarchyScenario.X;
    private static readonly Guid Q = new("a1341700-0000-4000-8000-0000000134a2");
    private static readonly Guid Y = new("a1341710-0000-4000-8000-0000000134b2");
    private static readonly Guid Admin = HierarchyScenario.UserOf(HierarchyScenario.P);

    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantScope Red() => TenantScope.Group(P, [C1, C2], GroupKind.Concesion);

    private CrearLoteConsolidadosHandler Handler(FlitDbContext ctx)
    {
        var procedimientos = new ProcedureInstanceRepository(ctx);
        return new CrearLoteConsolidadosHandler(
            new ConsolidadoLoteRepository(ctx),
            new LoteSeleccionResolverPorOrigen(
            [
                new TramitesSeleccionResolver(
                    new ListProcedureInstancesFilteredHandler(procedimientos), procedimientos,
                    new NetworkListProcedureInstancesHandler(procedimientos)),
            ]),
            new ConsolidadoLoteCipher(_dataProtection));
    }

    private static CrearLoteConsolidadosCommand AdminDeRed(LoteSeleccion seleccion, LoteAlcanceRed alcance) => new()
    {
        Origen = ConsolidadoExportOrigin.Tramites,
        TenantId = P,
        UsuarioId = Admin,
        RolCodigo = "AdminCompany",
        TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
        Seleccion = seleccion,
        ConfirmaEfectos = true,
        AlcanceRed = alcance,
    };

    private static ProcedureInstanceListRequest Preparados() => new() { Estados = [TramiteEstado.Preparado] };

    private static SeleccionPorFiltro FiltroPreparados() => new(new TramitesLoteFiltro(Preparados()));

    private async Task SembrarSettingsAsync(int? tope = null)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active) VALUES (uuidv7(), true);
            """);
        if (tope is { } t)
            await ExecAsync(cn, "UPDATE tramites.consolidado_export_settings SET max_items_per_batch = @t", ("t", t));
    }

    /// <summary><paramref name="cuantos"/> trámites <c>preparado</c> de <paramref name="tenant"/> (radicado único global).</summary>
    private async Task<IReadOnlyList<Guid>> SembrarPreparadosAsync(Guid tenant, int cuantos, int baseRadicado)
    {
        await using var ctx = NewContext();
        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < cuantos; i++)
        {
            var p = new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureTypeId = type,
                ReferenceNumber = $"{baseRadicado + i}",
                Status = TramiteEstado.Preparado,
                Plate = $"RD{baseRadicado % 1000:D3}{i:D2}",
                Vin = $"VINR13417{baseRadicado + i:D8}",
                CreatedByUserId = Admin,
                CreatedAt = now.AddMinutes(-i - 10),
            };
            ctx.ProcedureInstances.Add(p);
            ids.Add(p.Id);
        }

        await ctx.SaveChangesAsync(Ct);
        return ids;
    }

    /// <summary>Segunda cabeza Q con su hija Y: una red ajena a la de P.</summary>
    private async Task SembrarOtraRedAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Q, "IT-Q-13417", isGroupParent: true, parentId: null));
        await ctx.SaveChangesAsync(Ct);
        ctx.Tenants.Add(TenantSeed.New(Y, "IT-Y-13417", isGroupParent: false, parentId: Q));
        await ctx.SaveChangesAsync(Ct);
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_TodaLaRedEnModoFiltro_ElTotalEsElDeLaBusquedaDeRed_ConLaCompaniaDeCadaTramite()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        await SembrarPreparadosAsync(P, 3, 710000);
        await SembrarPreparadosAsync(C1, 2, 720000);
        await SembrarPreparadosAsync(C2, 2, 730000);
        await SembrarPreparadosAsync(X, 2, 740000);

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(AdminDeRed(FiltroPreparados(), new LoteAlcanceRed(Red(), null)), Ct);

        r.Creado.Should().BeTrue(r.Error);
        await using var db = NewContext();
        var (_, totalBusqueda, error) = await new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(db))
            .HandleAsync(Red(), null, Preparados(), Ct);
        error.Should().BeNull();
        totalBusqueda.Should().Be(7, "P (3) + C1 (2) + C2 (2); X no es de la red");
        r.Lote!.TotalItems.Should().Be(totalBusqueda, "el lote es exactamente lo que muestra POST /network/instances/search");

        var lote = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == r.Lote.Id, Ct);
        lote.Origin.Should().Be(ConsolidadoExportOrigin.Tramites);
        lote.TenantId.Should().Be(P);
        lote.NetworkScope.Should().BeTrue();
        lote.ScopeTenantId.Should().BeNull();

        var tenants = await db.ConsolidadoExportBatchItems.AsNoTracking()
            .Where(i => i.BatchId == lote.Id).Select(i => i.TenantId).ToListAsync(Ct);
        tenants.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count())
            .Should().BeEquivalentTo(new Dictionary<Guid, int> { [P] = 3, [C1] = 2, [C2] = 2 });

        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == lote.Id, Ct);
        audit.Event.Should().Be(ConsolidadoExportAuditEvent.LoteCreado);
        audit.ReachedTenantIds.Should().BeEquivalentTo([P, C1, C2]);
        JsonDocument.Parse(audit.FilterSummary!).RootElement.GetProperty("alcanceRed").GetString().Should().Be("red");
        (await ContarAsync("SELECT count(*) FROM tramites.network_access_audit")).Should().Be(0,
            "P3 = b: la descarga en lote no se registra en los accesos de red de la hija");
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_UnaHija_SoloEntranSusTramites_ScopeEsLaHija_YLaAuditoriaNoLlevaSuId()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        await SembrarPreparadosAsync(P, 2, 711000);
        await SembrarPreparadosAsync(C1, 3, 721000);
        await SembrarPreparadosAsync(C2, 1, 731000);
        var (efectivo, _) = NetworkScopePolicy.Narrow(Red(), C1);

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(AdminDeRed(FiltroPreparados(), new LoteAlcanceRed(efectivo!, C1)), Ct);

        r.Creado.Should().BeTrue(r.Error);
        await using var db = NewContext();
        var lote = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == r.Lote!.Id, Ct);
        lote.ScopeTenantId.Should().Be(C1);
        lote.NetworkScope.Should().BeTrue();
        lote.TotalItems.Should().Be(3);
        (await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == lote.Id).Select(i => i.TenantId).Distinct()
            .ToListAsync(Ct)).Should().Equal(C1);

        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == lote.Id, Ct);
        // jsonb normaliza el texto: se compara la clave, no la cadena.
        JsonDocument.Parse(audit.FilterSummary!).RootElement.GetProperty("alcanceRed").GetString().Should().Be("hija");
        audit.FilterSummary.Should().NotContain(C1.ToString()).And.NotContain("IT-C1");
        audit.ReachedTenantIds.Should().Equal(C1);
    }

    // ── AC3 + AC5 ───────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_CasillasAManoDeCabezaEHija_EntranLasDos()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var deP = HierarchyScenario.DeliveredProcedureOf(P);
        var deC1 = HierarchyScenario.DraftProcedureOf(C1);

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(AdminDeRed(new SeleccionPorIds([deP, deC1]), new LoteAlcanceRed(Red(), null)), Ct);

        r.Creado.Should().BeTrue(r.Error);
        await using var db = NewContext();
        (await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == r.Lote!.Id)
            .Select(i => i.ProcedureInstanceId).ToListAsync(Ct)).Should().BeEquivalentTo([deP, deC1]);
    }

    [PostgresFact]
    public async Task AC5_IdsInyectadosDeCompaniaAjenaYDeHijaDeOtraCabeza_NoEntranNiSeAuditan()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarOtraRedAsync();
        await SembrarSettingsAsync();
        var deY = (await SembrarPreparadosAsync(Y, 1, 750000)).Single();
        var deP = HierarchyScenario.DeliveredProcedureOf(P);
        var deX = HierarchyScenario.DeliveredProcedureOf(X);

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(AdminDeRed(new SeleccionPorIds([deP, deX, deY]), new LoteAlcanceRed(Red(), null)), Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(1);
        await using var db = NewContext();
        (await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == r.Lote.Id)
            .Select(i => i.ProcedureInstanceId).ToListAsync(Ct)).Should().Equal(deP);
        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == r.Lote.Id, Ct);
        audit.ReachedTenantIds.Should().Equal(P).And.NotContain(X).And.NotContain(Y);
    }

    // ── AC11 ────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC11_LaSeleccionDeRedSuperaElTope_422ConElTotalDeLaRed_SinCrearNada()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync(tope: 4);
        await SembrarPreparadosAsync(P, 3, 712000);
        await SembrarPreparadosAsync(C1, 3, 722000);

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(AdminDeRed(FiltroPreparados(), new LoteAlcanceRed(Red(), null)), Ct);

        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        r.Total.Should().Be(6, "COUNT con el mismo predicado de red");
        r.Tope.Should().Be(4);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(0);
    }

    // ── SQL ─────────────────────────────────────────────────────────────────────────────

    private async Task<long> ContarAsync(string sql)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }
}
