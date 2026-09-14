using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12361 (Feature #12257, épica #12235) — auditoría del acceso consolidado contra PostgreSQL real
/// sobre <see cref="HierarchyScenario"/> (P cabeza; C1/C2 hijos; X ajeno; S aislado). Reproduce el
/// encadenado de la API: handler <c>Network*</c> → <see cref="NetworkAccessAuditPolicy.ReachedChildren"/>
/// (la regla del endpoint filter) → <see cref="NetworkAccessAuditWriter"/> (scope DI propio) →
/// <see cref="NetworkAccessAuditReader"/> (consulta del hijo y del SuperAdmin).
/// <list type="bullet">
///   <item>AC1 — listado con hijos ⇒ UNA fila con actor, cabeza, los dos hijos, el recurso y el instante.</item>
///   <item>AC2 — detalle de un trámite de C1 ⇒ fila con <c>procedure_id</c> + dueño, consultable por C1.</item>
///   <item>AC3 — desvincular a C1 no altera ni oculta sus registros (sin FK).</item>
///   <item>AC4 — el SuperAdmin filtra por cliente, rango y recurso, con paginación acotada.</item>
///   <item>AC5 — consulta acotada a la propia cabeza ⇒ cero filas.</item>
///   <item>AC6 — las rutas viejas (S, sobrecarga <c>Guid?</c>) no escriben nada.</item>
///   <item>AC7 — <c>RecordAttachmentAccessAsync</c> deja el intento rechazado, consultable por el hijo.</item>
///   <item>AC8 — 300 trámites de dos hijos ⇒ un registro con dos ids y filtros sin placa/documento/nombre.</item>
///   <item>Append-only — UPDATE y DELETE rechazados por la base.</item>
/// </list>
/// Uso de ejemplo: <c>await Writer().WriteAsync(new NetworkAccessAuditEntry(user, P, [C1, C2], "network.instances.search", null, null, null, null, "ok"))</c>.
/// </summary>
public sealed class NetworkAccessAuditTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid HeadUser = HierarchyScenario.UserOf(HierarchyScenario.P);

    private static TenantScope GroupP() =>
        TenantScope.Group(HierarchyScenario.P, [HierarchyScenario.C1, HierarchyScenario.C2], GroupKind.Concesion);

    private static ProcedureInstanceListRequest Page(int? take = null, int skip = 0) =>
        new() { Skip = skip, Take = take ?? ListProcedureInstancesHandler.MaxItems };

    // ── AC1 — un registro por listado con hijos ───────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Listado_con_filas_de_dos_hijos_deja_un_registro_con_actor_cabeza_hijos_recurso_e_instante()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var (items, _, error) = await new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx))
            .HandleAsync(GroupP(), null, Page());
        error.Should().BeNull();

        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);
        await RecordListAsync(NetworkAccessVocabulary.Resources.InstancesSearch, GroupP(), items.Select(i => i.TenantId), Page(), null);

        var rows = await ctx.NetworkAccessAuditEntries.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle("un listado es UNA petición: un registro, no uno por trámite");
        var row = rows[0];
        row.ActorUserId.Should().Be(HeadUser);
        row.ActorTenantId.Should().Be(HierarchyScenario.P);
        row.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);
        row.ReachedTenantIds.Should().NotContain(HierarchyScenario.P, "la cabeza no es un hijo alcanzado");
        row.Resource.Should().Be(NetworkAccessVocabulary.Resources.InstancesSearch);
        row.Result.Should().Be(NetworkAccessVocabulary.Results.Ok);
        row.OccurredAt.Should().BeAfter(antes).And.BeBefore(DateTimeOffset.UtcNow.AddSeconds(5));
        row.ProcedureId.Should().BeNull();
        row.AttachmentId.Should().BeNull();
    }

    [PostgresFact]
    public async Task AC1_Estadisticas_del_universo_consolidado_registran_los_hijos_con_tramites_bajo_el_filtro()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var handler = new NetworkCountProcedureInstancesByStatusHandler(new ProcedureInstanceRepository(ctx));

        var (result, error) = await handler.HandleWithReachAsync(GroupP(), null, Page());
        error.Should().BeNull();
        result!.Counts[TramiteEstado.Borrador].Should().Be(3);
        result.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.P, HierarchyScenario.C1, HierarchyScenario.C2]);

        await RecordListAsync(NetworkAccessVocabulary.Resources.StatsOverview, GroupP(), result.ReachedTenantIds, Page(), null);

        var row = await ctx.NetworkAccessAuditEntries.AsNoTracking().SingleAsync();
        row.Resource.Should().Be(NetworkAccessVocabulary.Resources.StatsOverview);
        row.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);

        // Acotado a C2 ⇒ solo C2 alcanzado.
        var (soloC2, _) = await handler.HandleWithReachAsync(GroupP(), HierarchyScenario.C2, Page());
        soloC2!.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.C2]);
    }

    // ── AC2 — detalle: trámite + dueño, consultable por el hijo ──────────────────────────────

    [PostgresFact]
    public async Task AC2_Detalle_de_un_tramite_del_hijo_registra_el_tramite_y_el_hijo_lo_consulta_en_su_auditoria()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var id = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var (detalle, error) = await new NetworkGetProcedureInstanceHandler(new ProcedureInstanceRepository(ctx))
            .HandleAsync(id, GroupP());
        error.Should().BeNull();

        await Writer().WriteAsync(new NetworkAccessAuditEntry(
            HeadUser, HierarchyScenario.P,
            NetworkAccessAuditPolicy.ReachedChildren(GroupP(), [detalle!.TenantId], NetworkAccessVocabulary.Results.Ok),
            NetworkAccessVocabulary.Resources.InstancesDetail, null, id, detalle.TenantId, null, NetworkAccessVocabulary.Results.Ok));

        var row = await ctx.NetworkAccessAuditEntries.AsNoTracking().SingleAsync();
        row.ProcedureId.Should().Be(id);
        row.ProcedureTenantId.Should().Be(HierarchyScenario.C1);
        row.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.C1]);

        // El hijo C1 lo ve en SU auditoría; C2 no ve nada; X tampoco.
        var (deC1, totalC1) = await Reader(ctx).SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C1, null, null, null));
        var (deC2, _) = await Reader(ctx).SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C2, null, null, null));
        var (deX, _) = await Reader(ctx).SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.X, null, null, null));
        totalC1.Should().Be(1);
        deC1.Single().ProcedureId.Should().Be(id);
        deC1.Single().ActorUserId.Should().Be(HeadUser);
        deC2.Should().BeEmpty();
        deX.Should().BeEmpty();
    }

    // ── AC3 — sobrevive al desvínculo ─────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_Tras_desvincular_a_C1_sus_registros_siguen_integros_y_consultables()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var id = HierarchyScenario.DraftProcedureOf(HierarchyScenario.C1);
        await Writer().WriteAsync(new NetworkAccessAuditEntry(
            HeadUser, HierarchyScenario.P, [HierarchyScenario.C1], NetworkAccessVocabulary.Resources.InstancesDetail,
            null, id, HierarchyScenario.C1, null, NetworkAccessVocabulary.Results.Ok));
        await RecordListAsync(NetworkAccessVocabulary.Resources.InstancesSearch, GroupP(), [HierarchyScenario.C1, HierarchyScenario.C2], Page(), null);

        // SuperAdmin desvincula a C1 en la base (sin tocar token ni sesión).
        await using (var admin = NewContext())
        {
            var c1 = await admin.Tenants.SingleAsync(t => t.Id == HierarchyScenario.C1);
            c1.ParentTenantId = null;
            await admin.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        (await ctx.Tenants.AsNoTracking().SingleAsync(t => t.Id == HierarchyScenario.C1)).ParentTenantId.Should().BeNull();
        var (rows, total) = await Reader(ctx).SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C1, null, null, null));
        total.Should().Be(2, "los accesos anteriores al desvínculo se conservan íntegros");
        rows.Should().Contain(r => r.ProcedureId == id && r.ProcedureTenantId == HierarchyScenario.C1);
        rows.Should().Contain(r => r.Resource == NetworkAccessVocabulary.Resources.InstancesSearch && r.ReachedTenantIds.Contains(HierarchyScenario.C1));

        // Y la siguiente petición de P ya no alcanza a C1: no se genera registro sobre él.
        var scope = await NewResolver(ctx).ResolveAsync(HierarchyScenario.P);
        scope.ReadTenantIds.Should().NotContain(HierarchyScenario.C1);
        var (despues, _, _) = await new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx)).HandleAsync(scope, null, Page());
        await RecordListAsync(NetworkAccessVocabulary.Resources.InstancesSearch, scope, despues.Select(i => i.TenantId), Page(), null);
        var ultimo = await ctx.NetworkAccessAuditEntries.AsNoTracking().OrderByDescending(r => r.OccurredAt).FirstAsync();
        ultimo.ReachedTenantIds.Should().BeEquivalentTo([HierarchyScenario.C2]);
    }

    // ── AC4 — consulta del SuperAdmin ─────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_SuperAdmin_consulta_por_cliente_rango_y_recurso_con_paginacion_acotada()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var writer = Writer();
        for (var n = 0; n < 7; n++)
        {
            await writer.WriteAsync(new NetworkAccessAuditEntry(
                HeadUser, HierarchyScenario.P, [HierarchyScenario.C1], NetworkAccessVocabulary.Resources.InstancesSearch,
                "{\"take\":50}", null, null, null, NetworkAccessVocabulary.Results.Ok));
        }
        await writer.RecordAttachmentAccessAsync(
            HeadUser, HierarchyScenario.P, HierarchyScenario.C2, HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2),
            Guid.NewGuid(), NetworkAccessVocabulary.Resources.AttachmentsDownload, NetworkAccessVocabulary.Results.Ok);

        await using var ctx = NewContext();
        var reader = Reader(ctx);

        var (todos, total) = await reader.SearchAsync(new NetworkAccessAuditQuery(null, null, null, null));
        total.Should().Be(8);
        todos.Should().HaveCount(8).And.BeInDescendingOrder(r => r.OccurredAt);

        var (deC1, totalC1) = await reader.SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C1, null, null, null, Page: 2, Take: 3));
        totalC1.Should().Be(7);
        deC1.Should().HaveCount(3, "página 2 de 3 en 3");
        deC1.Should().OnlyContain(r => r.ActorTenantId == HierarchyScenario.P && r.ActorUserId == HeadUser);

        var (descargas, _) = await reader.SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C2, null, null, NetworkAccessVocabulary.Resources.AttachmentsDownload));
        descargas.Should().ContainSingle().Which.AttachmentId.Should().NotBeNull();

        var (futuro, totalFuturo) = await reader.SearchAsync(new NetworkAccessAuditQuery(null, DateTimeOffset.UtcNow.AddHours(1), null, null));
        totalFuturo.Should().Be(0);
        futuro.Should().BeEmpty();

        var (tope, _) = await reader.SearchAsync(new NetworkAccessAuditQuery(null, null, null, null, Take: 10_000));
        tope.Should().HaveCount(8);
        new NetworkAccessAuditQuery(null, null, null, null, Take: 10_000).EffectiveTake.Should().Be(NetworkAccessAuditQuery.MaxTake);
        new NetworkAccessAuditQuery(null, null, null, null, Page: 0, Take: 0).EffectivePage.Should().Be(1);
    }

    // ── AC5 — datos propios no inflan el registro ─────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_Consulta_acotada_a_la_propia_cabeza_o_sin_filas_de_hijos_no_genera_registro()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var handler = new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx));

        var (soloP, _, _) = await handler.HandleAsync(GroupP(), HierarchyScenario.P, Page());
        soloP.Should().OnlyContain(i => i.TenantId == HierarchyScenario.P);
        await RecordListAsync(NetworkAccessVocabulary.Resources.InstancesSearch, GroupP(), soloP.Select(i => i.TenantId), Page(), HierarchyScenario.P);

        var (vacio, _, _) = await handler.HandleAsync(GroupP(), null, Page() with { OrganismoTransito = "NINGUN-ORGANISMO" });
        vacio.Should().BeEmpty();
        await RecordListAsync(NetworkAccessVocabulary.Resources.InstancesSearch, GroupP(), vacio.Select(i => i.TenantId), Page(), null);

        // Ni siquiera aunque un llamante intentara colar a la cabeza como «alcanzada».
        await Writer().WriteAsync(new NetworkAccessAuditEntry(
            HeadUser, HierarchyScenario.P, [HierarchyScenario.P], NetworkAccessVocabulary.Resources.InstancesSearch,
            null, null, null, null, NetworkAccessVocabulary.Results.Ok));

        (await ctx.NetworkAccessAuditEntries.CountAsync()).Should().Be(0);
    }

    // ── AC6 — sin regresión: rutas viejas y clientes sin jerarquía ────────────────────────────

    [PostgresFact]
    public async Task AC6_Las_rutas_viejas_de_un_cliente_sin_jerarquia_no_escriben_en_la_tabla()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var single = await NewResolver(ctx).ResolveAsync(HierarchyScenario.S);
        var adminAuditAntes = await ctx.TenantConfigAuditLogs.CountAsync();

        var (viejas, total) = await new ListProcedureInstancesFilteredHandler(repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = HierarchyScenario.S });
        var (detalle, _) = await new GetProcedureInstanceHandler(repo)
            .HandleAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.S), HierarchyScenario.S);
        var conteos = await new CountProcedureInstancesByStatusHandler(repo)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = HierarchyScenario.S });
        total.Should().Be(2);
        detalle.Should().NotBeNull();
        conteos.Should().NotBeEmpty();

        // Aunque la policy se evaluara para S (Single), no hay hijos: nada que registrar.
        NetworkAccessAuditPolicy.ReachedChildren(single, viejas.Select(i => i.TenantId), NetworkAccessVocabulary.Results.Ok).Should().BeEmpty();
        NetworkAccessAuditPolicy.ReachedChildren(TenantScope.All(), [HierarchyScenario.C1], NetworkAccessVocabulary.Results.Ok).Should().BeEmpty("SuperAdmin no audita aquí");

        (await ctx.NetworkAccessAuditEntries.CountAsync()).Should().Be(0);
        (await ctx.TenantConfigAuditLogs.CountAsync()).Should().Be(adminAuditAntes, "la auditoría administrativa existente no cambia de volumen");
    }

    // ── AC7 — descargas: punto de entrada reutilizable e intentos rechazados ─────────────────

    [PostgresFact]
    public async Task AC7_RecordAttachmentAccessAsync_registra_la_descarga_y_el_intento_rechazado_consultables_por_el_hijo()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var procedure = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var attachment = Guid.NewGuid();
        var writer = Writer();

        await writer.RecordAttachmentAccessAsync(HeadUser, HierarchyScenario.P, HierarchyScenario.C1, procedure, attachment,
            NetworkAccessVocabulary.Resources.AttachmentsDownload, NetworkAccessVocabulary.Results.Ok);
        await writer.RecordAttachmentAccessAsync(HeadUser, HierarchyScenario.P, HierarchyScenario.C1, procedure, attachment,
            NetworkAccessVocabulary.Resources.AttachmentsDownload, NetworkAccessVocabulary.Results.Forbidden);
        await writer.RecordAttachmentAccessAsync(HeadUser, HierarchyScenario.P, HierarchyScenario.C1, procedure, null,
            NetworkAccessVocabulary.Resources.AttachmentsList, NetworkAccessVocabulary.Results.Ok);

        await using var ctx = NewContext();
        var (deC1, total) = await Reader(ctx).SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C1, null, null, null));
        total.Should().Be(3);
        deC1.Should().OnlyContain(r => r.ProcedureId == procedure && r.ProcedureTenantId == HierarchyScenario.C1 && r.ActorTenantId == HierarchyScenario.P);
        deC1.Should().Contain(r => r.Resource == NetworkAccessVocabulary.Resources.AttachmentsDownload && r.Result == NetworkAccessVocabulary.Results.Forbidden && r.AttachmentId == attachment);
        deC1.Should().Contain(r => r.Resource == NetworkAccessVocabulary.Resources.AttachmentsDownload && r.Result == NetworkAccessVocabulary.Results.Ok);
        deC1.Should().Contain(r => r.Resource == NetworkAccessVocabulary.Resources.AttachmentsList && r.AttachmentId == null);
    }

    // ── AC8 — volumen y contenido ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_Trescientos_tramites_de_dos_hijos_dejan_un_registro_con_dos_ids_y_filtros_sin_datos_personales()
    {
        var type = await HierarchyScenario.SeedAsync(Fixture);
        await using (var seed = NewContext())
        {
            SeedExtraDrafts(seed, HierarchyScenario.C1, type.Id, 150);
            SeedExtraDrafts(seed, HierarchyScenario.C2, type.Id, 150);
            await seed.SaveChangesAsync();
        }

        await using var ctx = NewContext();
        var handler = new NetworkListProcedureInstancesHandler(new ProcedureInstanceRepository(ctx));
        // Filtro con un dato personal (nombre del vendedor sembrado en los 300 borradores): debe
        // aplicarse a la consulta y NO aparecer en el registro.
        var request = Page(take: ListProcedureInstancesHandler.MaxItems) with
        {
            Estados = [TramiteEstado.Borrador],
            Vendedor = "Juan Pérez",
            SortBy = "createdAt",
        };
        var reached = new HashSet<Guid>();
        var paginas = 0;
        for (var skip = 0; ; skip += ListProcedureInstancesHandler.MaxItems)
        {
            var (items, total, _) = await handler.HandleAsync(GroupP(), null, request with { Skip = skip });
            paginas++;
            foreach (var i in items) reached.Add(i.TenantId);
            if (skip + ListProcedureInstancesHandler.MaxItems >= total) break;
        }
        paginas.Should().BeGreaterThan(1, "el listado se sirve en varias páginas");

        // La API registra UNA fila por petición HTTP: aquí una sola «petición» que devolvió 300 trámites.
        await RecordListAsync(NetworkAccessVocabulary.Resources.InstancesSearch, GroupP(), reached, request, null);

        var rows = await ctx.NetworkAccessAuditEntries.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle("un registro por petición, no por trámite");
        rows[0].ReachedTenantIds.Should().HaveCount(2).And.BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);
        rows[0].Filters.Should().NotBeNull();
        // jsonb normaliza el espaciado: se afirma sobre el documento, no sobre el texto.
        using var filters = System.Text.Json.JsonDocument.Parse(rows[0].Filters!);
        filters.RootElement.GetProperty("estados").EnumerateArray().Select(e => e.GetString()).Should().Equal(TramiteEstado.Borrador);
        filters.RootElement.GetProperty("take").GetInt32().Should().Be(ListProcedureInstancesHandler.MaxItems);
        // Solo identificadores: los filtros de texto se citan por nombre, jamás por valor (la lista
        // blanca exacta de la API se prueba en NetworkAccessAuditEndpointsTests.FiltersJson_*).
        filters.RootElement.GetProperty("textFilters").EnumerateArray().Select(e => e.GetString()).Should().Equal("vendedor");
        rows[0].Filters.Should().NotContain("Juan").And.NotContain("Pérez").And.NotContain("NET0");
        var pii = new[] { HierarchyScenario.SharedPlate, "Vendedor", "Comprador", "@flit.test" };
        foreach (var r in rows)
        {
            (r.Filters ?? string.Empty).Should().NotContainAny(pii);
        }
    }

    // ── Append-only ───────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AppendOnly_UPDATE_y_DELETE_son_rechazados_por_la_base()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await Writer().WriteAsync(new NetworkAccessAuditEntry(
            HeadUser, HierarchyScenario.P, [HierarchyScenario.C1], NetworkAccessVocabulary.Resources.InstancesSearch,
            null, null, null, null, NetworkAccessVocabulary.Results.Ok));

        await using var connection = await Fixture.OpenConnectionAsync();
        await using (var update = new NpgsqlCommand("UPDATE tramites.network_access_audit SET result = 'forbidden'", connection))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
            ex.MessageText.Should().Contain("append-only");
        }
        await using (var delete = new NpgsqlCommand("DELETE FROM tramites.network_access_audit", connection))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
            ex.MessageText.Should().Contain("append-only");
        }
        await using (var vacio = new NpgsqlCommand(
            "INSERT INTO tramites.network_access_audit (actor_tenant_id, reached_tenant_ids, resource, result) VALUES (@p, '{}', 'network.instances.search', 'ok')", connection))
        {
            vacio.Parameters.AddWithValue("p", HierarchyScenario.P);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => vacio.ExecuteNonQueryAsync());
            ex.ConstraintName.Should().Be("ck_network_access_audit_reached_not_empty");
        }

        await using var ctx = NewContext();
        var row = await ctx.NetworkAccessAuditEntries.AsNoTracking().SingleAsync();
        row.Result.Should().Be(NetworkAccessVocabulary.Results.Ok, "la historia no se reescribe");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Lo que hace la API tras un listado/estadística: policy del filtro + escritura best-effort.</summary>
    private Task RecordListAsync(string resource, TenantScope scope, IEnumerable<Guid> present, ProcedureInstanceListRequest request, Guid? childTenantId)
    {
        var reached = NetworkAccessAuditPolicy.ReachedChildren(scope, present, NetworkAccessVocabulary.Results.Ok);
        if (reached.Count == 0)
            return Task.CompletedTask;

        var filters = System.Text.Json.JsonSerializer.Serialize(new
        {
            childTenantId,
            estados = request.Estados,
            sortBy = request.SortBy,
            take = request.Take,
            textFilters = new[] { ("placa", request.Placa), ("vendedor", request.Vendedor), ("comprador", request.Comprador), ("busqueda", request.Busqueda) }
                .Where(f => !string.IsNullOrWhiteSpace(f.Item2)).Select(f => f.Item1).ToArray(),
        }, FiltersJsonOptions);

        return Writer().WriteAsync(new NetworkAccessAuditEntry(
            HeadUser, scope.WriteTenantId!.Value, reached, resource, filters, null, null, null, NetworkAccessVocabulary.Results.Ok));
    }

    private static readonly System.Text.Json.JsonSerializerOptions FiltersJsonOptions =
        new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

    /// <summary>Writer real con scope DI propio sobre la base efímera (igual que en producción).</summary>
    private NetworkAccessAuditWriter Writer()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FlitDbContext>(o => o
            .UseNpgsql(Fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        var provider = services.BuildServiceProvider();
        return new NetworkAccessAuditWriter(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<NetworkAccessAuditWriter>.Instance);
    }

    private static NetworkAccessAuditReader Reader(FlitDbContext ctx) => new(ctx);

    private static void SeedExtraDrafts(FlitDbContext ctx, Guid tenant, Guid typeId, int count)
    {
        var user = HierarchyScenario.UserOf(tenant);
        var code = HierarchyScenario.CodeOf(tenant);
        for (var n = 0; n < count; n++)
        {
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureTypeId = typeId,
                ReferenceNumber = $"{code}A{n:D5}",
                Status = TramiteEstado.Borrador,
                Plate = $"NET{n % 1000:D3}",
                VendedorNombre = "Vendedor Juan Pérez",
                CreatedByUserId = user,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-n),
            });
        }
    }

    private static DbTenantScopeResolver NewResolver(FlitDbContext ctx) =>
        new(ctx, new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance), NullLogger<DbTenantScopeResolver>.Instance);
}
