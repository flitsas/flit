using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain;
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
/// HU #13383 (Feature #13307, épica #13216, ADR-0070 A4.1/A4.6) — lote del Super Admin contra PostgreSQL real:
/// <see cref="CrearLoteConsolidadosHandler"/> + <see cref="ConsolidadoLoteRepository"/> + los resolvers reales
/// (<see cref="TramitesSeleccionResolver"/> y <see cref="SuperAdminSeleccionResolver"/>). Cubre el lote sin compañía
/// con ítems de dos compañías (AC1), la condición <c>compania</c> del filtro (AC2), el acotamiento por
/// <c>X-Tenant-Id</c> (AC3), el maestro admitido en este origen (AC4) y la paridad de 409/503 (AC8).
/// Compañías A = <see cref="HierarchyScenario.C1"/>, B = <see cref="HierarchyScenario.C2"/>.
/// <para>Uso de ejemplo:
/// <code>
/// var r = await Handler(ctx).HandleAsync(new CrearLoteConsolidadosCommand
/// { Origen = "superadmin", TenantId = null, ScopeTenantId = B, UsuarioId = sa, RolCodigo = "SuperAdmin", ... }, ct);
/// </code></para>
/// </summary>
public sealed class CrearLoteSuperAdminTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid A = HierarchyScenario.C1;
    private static readonly Guid B = HierarchyScenario.C2;

    /// <summary>Actor del Super Admin: un usuario sembrado (la FK exige que exista); el rol lo pone el endpoint.</summary>
    private static readonly Guid SuperAdmin = HierarchyScenario.UserOf(HierarchyScenario.P);

    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CrearLoteConsolidadosHandler Handler(FlitDbContext ctx)
    {
        var procedimientos = new ProcedureInstanceRepository(ctx);
        var listado = new ListProcedureInstancesFilteredHandler(procedimientos);
        return new CrearLoteConsolidadosHandler(
            new ConsolidadoLoteRepository(ctx),
            new LoteSeleccionResolverPorOrigen(
            [
                new TramitesSeleccionResolver(listado, procedimientos),
                new SuperAdminSeleccionResolver(listado, procedimientos),
            ]),
            new ConsolidadoLoteCipher(_dataProtection));
    }

    private static CrearLoteConsolidadosCommand Sa(LoteSeleccion seleccion, Guid? scope = null, string? tipo = null) => new()
    {
        Origen = ConsolidadoExportOrigin.Superadmin,
        TenantId = null,
        ScopeTenantId = scope,
        UsuarioId = SuperAdmin,
        RolCodigo = "SuperAdmin",
        TipoDocumento = tipo ?? ConsolidadoExportDocumentType.Consolidado,
        Seleccion = seleccion,
        ConfirmaEfectos = true,
        ClientIp = System.Net.IPAddress.Parse("10.13.38.3"),
        UserAgent = "pruebas-13383",
    };

    /// <summary>El escenario base solo siembra <c>entregado</c>/<c>borrador</c>: el filtro abarca solo lo de la prueba.</summary>
    private static SeleccionPorFiltro Preparados(IReadOnlyList<QueryCondition>? condiciones = null) =>
        new(new TramitesLoteFiltro(new ProcedureInstanceListRequest
        {
            Estados = [TramiteEstado.Preparado],
            Condiciones = condiciones,
        }), []);

    private async Task SembrarSettingsAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active) VALUES (uuidv7(), true);
            """);
    }

    private async Task<IReadOnlyList<Guid>> SembrarTramitesAsync(Guid tenant, int cuantos, int desde)
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
                ReferenceNumber = $"{738300 + desde + i}",
                Status = TramiteEstado.Preparado,
                Plate = $"QSA{desde + i:D3}",
                Vin = $"VINSA13383{desde + i:D7}",
                CreatedByUserId = HierarchyScenario.UserOf(tenant),
                CreatedAt = now.AddMinutes(-(desde + i) - 10),
            };
            ctx.ProcedureInstances.Add(p);
            ids.Add(p.Id);
        }

        await ctx.SaveChangesAsync(Ct);
        return ids;
    }

    /// <summary>Siete trámites «preparado» en A y cinco en B (12 en total).</summary>
    private async Task<(IReadOnlyList<Guid> DeA, IReadOnlyList<Guid> DeB)> SembrarDosCompaniasAsync()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var deA = await SembrarTramitesAsync(A, 7, 0);
        var deB = await SembrarTramitesAsync(B, 5, 100);
        return (deA, deB);
    }

    private async Task<long> ContarAsync(string sql, params (string Name, object? Value)[] args)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_FiltroQueResuelveDoceDeAyB_LoteSinCompania_ItemsConSuCompania_AuditoriaConAmbas()
    {
        var (deA, deB) = await SembrarDosCompaniasAsync();

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Sa(Preparados()), Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.Status.Should().Be(ConsolidadoExportStatus.EnCola);
        r.Lote.TotalItems.Should().Be(12);

        await using var db = NewContext();
        var lote = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == r.Lote.Id, Ct);
        lote.Origin.Should().Be(ConsolidadoExportOrigin.Superadmin);
        lote.TenantId.Should().BeNull("el lote del Super Admin no tiene compañía (Q8)");
        lote.ScopeTenantId.Should().BeNull();
        lote.TotalItems.Should().Be(12);
        lote.RequestedRoleCode.Should().Be("SuperAdmin");

        var items = await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == lote.Id).ToListAsync(Ct);
        items.Should().HaveCount(12);
        items.Where(i => i.TenantId == A).Select(i => i.ProcedureInstanceId).Should().BeEquivalentTo(deA);
        items.Where(i => i.TenantId == B).Select(i => i.ProcedureInstanceId).Should().BeEquivalentTo(deB);

        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == lote.Id, Ct);
        audit.Event.Should().Be(ConsolidadoExportAuditEvent.LoteCreado);
        audit.Origin.Should().Be(ConsolidadoExportOrigin.Superadmin);
        audit.ActorTenantId.Should().BeNull();
        audit.ActorUserId.Should().Be(SuperAdmin);
        audit.ScopeTenantId.Should().BeNull();
        audit.ReachedTenantIds.Should().BeEquivalentTo([A, B]);
        audit.TotalItems.Should().Be(12);
        audit.ActorRoleCode.Should().Be("SuperAdmin");
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_CondicionCompaniaB_SoloEntranTramitesDeB_ReachedB_YFilterSummarySinIds()
    {
        var (_, deB) = await SembrarDosCompaniasAsync();
        var compania = new QueryCondition(TramitesQueryFieldCatalog.Compania, QueryOperator.EsAlguno, [B.ToString()]);

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Sa(Preparados([compania])), Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(5);
        await using var db = NewContext();
        var items = await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == r.Lote.Id).ToListAsync(Ct);
        items.Select(i => i.ProcedureInstanceId).Should().BeEquivalentTo(deB);
        items.Should().OnlyContain(i => i.TenantId == B);

        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == r.Lote.Id, Ct);
        audit.ReachedTenantIds.Should().Equal(B);
        audit.FilterSummary.Should().NotContain(B.ToString()).And.NotContain(B.ToString("N"),
            "los ids de compañía ya están en reached_tenant_ids; filter_summary solo guarda el conteo");
        var condiciones = JsonDocument.Parse(audit.FilterSummary!).RootElement.GetProperty("filtro").GetProperty("condiciones");
        condiciones.GetArrayLength().Should().Be(1);
        condiciones[0].GetProperty("campo").GetString().Should().Be("compania");
        condiciones[0].GetProperty("operador").GetString().Should().Be(QueryOperator.EsAlguno);
        condiciones[0].GetProperty("cantidad").GetInt32().Should().Be(1);
        condiciones[0].TryGetProperty("valores", out _).Should().BeFalse();
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_ScopeB_IdsConUnTramiteDeA_ElDeAQuedaFuera_ScopeB_ReachedB()
    {
        var (deA, deB) = await SembrarDosCompaniasAsync();
        var cuerpo = new List<Guid> { deA[0], deB[0], deB[1] };

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Sa(new SeleccionPorIds(cuerpo), scope: B), Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(2, "el trámite de A no cuenta en el total");
        await using var db = NewContext();
        var lote = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == r.Lote.Id, Ct);
        lote.ScopeTenantId.Should().Be(B);
        lote.TenantId.Should().BeNull();
        var items = await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == lote.Id).ToListAsync(Ct);
        items.Select(i => i.ProcedureInstanceId).Should().BeEquivalentTo([deB[0], deB[1]]);
        items.Select(i => i.ProcedureInstanceId).Should().NotContain(deA[0]);
        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == lote.Id, Ct);
        audit.ScopeTenantId.Should().Be(B);
        audit.ReachedTenantIds.Should().Equal(B);
        audit.IdsCount.Should().Be(3);
    }

    // ── AC4 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_SuperAdminConConsolidadoMaestro_ElLoteSeCreaConEseTipo()
    {
        await SembrarDosCompaniasAsync();

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Sa(Preparados(), tipo: ConsolidadoExportDocumentType.ConsolidadoMaestro), Ct);

        r.Creado.Should().BeTrue(r.Error);
        await using var db = NewContext();
        (await db.ConsolidadoExportBatches.AsNoTracking().Where(b => b.Id == r.Lote!.Id).Select(b => b.DocumentType).SingleAsync(Ct))
            .Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
        (await db.ConsolidadoExportAuditEntries.AsNoTracking().Where(a => a.BatchId == r.Lote!.Id).Select(a => a.DocumentType).SingleAsync(Ct))
            .Should().Be(ConsolidadoExportDocumentType.ConsolidadoMaestro);
    }

    // ── AC8 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_SuperAdminConLoteEnProceso_LoteActivoConSuId_YNoCreaNada()
    {
        await SembrarDosCompaniasAsync();
        var activo = Guid.CreateVersion7();
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn,
                """
                INSERT INTO tramites.consolidado_export_batches
                  (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
                   total_items, dek_wrapped, effects_acknowledged_at, created_by)
                VALUES (@l, NULL, @u, 'SuperAdmin', 'superadmin', 'consolidado', 'filtro', 'en_proceso', 3, '\x0a'::bytea, now(), @u);
                """, ("l", activo), ("u", SuperAdmin));
        }

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Sa(Preparados()), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteActivo);
        r.LoteActivoId.Should().Be(activo);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(1);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batch_items")).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC8_SiFallaLaAuditoria_LoteNoCreado_SinLoteItemsNiAuditoria()
    {
        await SembrarDosCompaniasAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            CREATE OR REPLACE FUNCTION public.tmp_falla_auditoria_13383() RETURNS trigger AS $$
            BEGIN
                IF NEW.event = 'lote_creado' THEN RAISE EXCEPTION 'auditoria no disponible (prueba HU13383)'; END IF;
                RETURN NEW;
            END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER tmp_falla_auditoria_13383 BEFORE INSERT ON tramites.consolidado_export_audit
                FOR EACH ROW EXECUTE FUNCTION public.tmp_falla_auditoria_13383();
            """);
        CrearLoteConsolidadosResultado r;
        try
        {
            await using var ctx = NewContext();
            r = await Handler(ctx).HandleAsync(Sa(Preparados()), Ct);
        }
        finally
        {
            await ExecAsync(cn,
                """
                DROP TRIGGER IF EXISTS tmp_falla_auditoria_13383 ON tramites.consolidado_export_audit;
                DROP FUNCTION IF EXISTS public.tmp_falla_auditoria_13383();
                """);
        }

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteNoCreado);
        r.Mensaje.Should().Be(CrearLoteConsolidadosHandler.MensajeNoDisponible);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batch_items")).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }
}
