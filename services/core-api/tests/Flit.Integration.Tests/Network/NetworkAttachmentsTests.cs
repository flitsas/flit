using System.Text.Json;
using Flit.Infrastructure.Auditing;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12410 (Feature #12257, épica #12235) — documentos de la red contra PostgreSQL real sobre
/// <see cref="HierarchyScenario"/> (P cabeza — sembrada como MARCA_BLANCA o CONCESION según el AC; C1/C2 hijos; X ajeno;
/// S aislado), con un anexo por trámite entregado de cada cliente y un almacenamiento en memoria.
/// Reproduce el encadenado de la API: <c>DbTenantScopeResolver</c> (alcance + interruptor de grupo) →
/// <see cref="NetworkAttachmentsHandler"/> (dueño real por <see cref="ProcedureInstanceOwnerLookup"/>,
/// interruptor real por <see cref="DbHierarchySwitches"/>, handlers de anexos con el tenant del dueño) →
/// la regla de publicación del endpoint + <see cref="NetworkAccessAuditPolicy.ReachedChildren"/> →
/// <see cref="NetworkAccessAuditWriter"/> (Q38/Q39 del inventario).
/// <list type="bullet">
///   <item>AC1 — metadatos del trámite de C1 sin dirección alguna (el DTO no tiene <c>storagePath</c> ni URL).</item>
///   <item>AC2 — descarga por transmisión: los bytes del anexo de C1; el tenant que llega al repositorio es C1.</item>
///   <item>AC3 — escritura: <c>CanWrite</c> falso sobre los hijos; las 10 rutas de documentos las cubre <c>NetworkWriteRejectionTests</c>.</item>
///   <item>AC4 — cada listado/descarga (ok, forbidden, not_found) deja UNA fila; sobrevive al desvínculo de C1.</item>
///   <item>AC5 — CONCESION ⇒ <c>network_documents_disabled</c> con el interruptor apagado (seed); encendido ⇒ igual que MARCA_BLANCA.</item>
///   <item>AC6 — hijo→hermano/cabeza/ajeno y cabeza→ajeno/inexistente ⇒ misma respuesta que un recurso inexistente; conjunto vacío ⇒ nada.</item>
///   <item>AC7 — <c>group_read_scope</c> apagado ⇒ el alcance degrada a Single ⇒ <c>network_scope_required</c>.</item>
///   <item>AC8 — S usa los handlers de siempre con el mismo resultado y sin filas de auditoría de red.</item>
///   <item>AC9 — escenario completo con tres clientes: exactamente un registro por acceso.</item>
/// </list>
/// Uso de ejemplo: <c>await Handler(ctx).DownloadAsync(DeliveredProcedureOf(C1), AttachmentOf(C1), await ScopeOf(ctx, P))</c>.
/// </summary>
public sealed class NetworkAttachmentsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid HeadUser = HierarchyScenario.UserOf(HierarchyScenario.P);

    /// <summary>Anexo sembrado en el trámite entregado de cada cliente (<c>a0000000-000c-…</c>).</summary>
    private static Guid AttachmentOf(Guid tenant) =>
        new($"a0000000-000c-4000-8000-00000000{tenant.ToString("N")[^4..^2]}01");

    private static byte[] BytesOf(Guid tenant) => System.Text.Encoding.UTF8.GetBytes($"%PDF documento de {HierarchyScenario.CodeOf(tenant)}");

    // ── AC1 — metadatos sin direcciones ───────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_MarcaBlanca_lista_los_metadatos_del_tramite_del_hijo_sin_direccion_prefirmada_ni_storagePath()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        var scope = await ScopeOf(ctx, HierarchyScenario.P);
        scope.GroupKind.Should().Be(GroupKind.MarcaBlanca);

        var outcome = await Handler(ctx).ListAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), scope);

        outcome.Error.Should().BeNull();
        outcome.ProcedureTenantId.Should().Be(HierarchyScenario.C1);
        var dto = outcome.Result!.Attachments.Should().ContainSingle().Which;
        dto.Id.Should().Be(AttachmentOf(HierarchyScenario.C1));
        dto.Tipo.Should().Be("factura");
        dto.Filename.Should().Be("factura-C1.pdf");
        dto.Mimetype.Should().Be("application/pdf");
        dto.SizeBytes.Should().Be(BytesOf(HierarchyScenario.C1).Length);
        dto.UploadedAt.Should().BeBefore(DateTimeOffset.UtcNow);
        dto.DigitallySigned.Should().BeFalse();
        // El contrato no tiene «versión»: el modelo de anexos no la modela (se documenta, no se inventa).
        typeof(AttachmentDto).GetProperties().Select(p => p.Name).Should().NotContain(
            ["StoragePath", "Url", "PreviewUrl", "Version"], "ni ruta de almacenamiento, ni URL, ni versión");
        JsonSerializer.Serialize(outcome.Result).Should().NotContainAny("storage", "http", "url", "X-Amz");

        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsList, HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), null, outcome);
        var row = await ctx.NetworkAccessAuditEntries.AsNoTracking().SingleAsync();
        row.Resource.Should().Be(NetworkAccessVocabulary.Resources.AttachmentsList);
        row.Result.Should().Be(NetworkAccessVocabulary.Results.Ok);
        row.ProcedureTenantId.Should().Be(HierarchyScenario.C1);
        row.AttachmentId.Should().BeNull();
    }

    // ── AC2 — descarga por transmisión ────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_MarcaBlanca_descarga_el_binario_del_hijo_por_transmision_con_el_tenant_del_dueno()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        var scope = await ScopeOf(ctx, HierarchyScenario.P);
        var storage = new InMemoryStorage();
        var procedure = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);

        var outcome = await Handler(ctx, storage).DownloadAsync(procedure, AttachmentOf(HierarchyScenario.C1), scope);

        outcome.Error.Should().BeNull();
        outcome.ProcedureTenantId.Should().Be(HierarchyScenario.C1);
        outcome.Result!.Mimetype.Should().Be("application/pdf");
        outcome.Result.Filename.Should().Be("factura-C1.pdf");
        using var buffer = new MemoryStream();
        await outcome.Result.Content.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(BytesOf(HierarchyScenario.C1));
        storage.Opened.Should().Equal("store/C1/factura.pdf");

        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsDownload, procedure, AttachmentOf(HierarchyScenario.C1), outcome);
        var row = await ctx.NetworkAccessAuditEntries.AsNoTracking().SingleAsync();
        row.Resource.Should().Be(NetworkAccessVocabulary.Resources.AttachmentsDownload);
        row.AttachmentId.Should().Be(AttachmentOf(HierarchyScenario.C1));
        row.ProcedureId.Should().Be(procedure);
        row.ReachedTenantIds.Should().Equal(HierarchyScenario.C1);
    }

    // ── AC3 — escritura rechazada ─────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_El_alcance_de_red_no_otorga_escritura_sobre_los_documentos_de_los_hijos()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        var scope = await ScopeOf(ctx, HierarchyScenario.P);

        // La comprobación que aplica TenantWriteGuard (middleware) a las 10 rutas de escritura de
        // documentos — upload, presign, register, delete, generate-impronta, generate-rues,
        // impronta-diferida, fur, consolidado y admin consolidado limpiar/cargar — con una prueba
        // negativa por ruta en Flit.Admin.Tests/Tramites/NetworkWriteRejectionTests (AC4 de #12358).
        scope.CanRead(HierarchyScenario.C1).Should().BeTrue();
        scope.CanWrite(HierarchyScenario.C1).Should().BeFalse();
        scope.CanWrite(HierarchyScenario.C2).Should().BeFalse();
        scope.CanWrite(HierarchyScenario.P).Should().BeTrue("la cabeza sigue escribiendo sobre sí misma");
        var owner = await new ProcedureInstanceOwnerLookup(ctx).GetOwnerTenantIdAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1));
        owner.Should().Be(HierarchyScenario.C1, "el guard resuelve el dueño real y decide por CanWrite");
    }

    // ── AC5 — clase CONCESION cerrada por interruptor ─────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_Concesion_recibe_network_documents_disabled_con_el_interruptor_apagado_y_lee_con_el_encendido()
    {
        await SeedAsync(headKind: GroupKindCodes.Concesion);
        await using var ctx = NewContext();
        var scope = await ScopeOf(ctx, HierarchyScenario.P);
        scope.GroupKind.Should().Be(GroupKind.Concesion);
        var switches = new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance);
        (await switches.IsNetworkDocumentsConcesionEnabledAsync()).Should().BeFalse("seed de la migración 114-: apagado por defecto");
        var procedure = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var storage = new InMemoryStorage();

        var listOff = await Handler(ctx, storage).ListAsync(procedure, scope);
        var downloadOff = await Handler(ctx, storage).DownloadAsync(procedure, AttachmentOf(HierarchyScenario.C1), scope);
        listOff.Error.Should().Be(NetworkDocumentsPolicy.DocumentsDisabled);
        downloadOff.Error.Should().Be(NetworkDocumentsPolicy.DocumentsDisabled);
        listOff.ProcedureTenantId.Should().Be(HierarchyScenario.C1, "el intento se imputa al hijo para auditarlo");
        storage.Opened.Should().BeEmpty("nada llegó al almacenamiento");
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsDownload, procedure, AttachmentOf(HierarchyScenario.C1), downloadOff);

        // El SuperAdmin enciende el interruptor (misma operación que PUT /admin/platform/hierarchy-switches/{key}).
        var state = await switches.SetAsync(IHierarchySwitches.NetworkDocumentsConcesionKey, true, HeadUser);
        state!.IsEnabled.Should().BeTrue();

        var listOn = await Handler(ctx, storage).ListAsync(procedure, scope);
        var downloadOn = await Handler(ctx, storage).DownloadAsync(procedure, AttachmentOf(HierarchyScenario.C1), scope);
        listOn.Error.Should().BeNull();
        listOn.Result!.Attachments.Should().ContainSingle();
        downloadOn.Error.Should().BeNull();
        storage.Opened.Should().ContainSingle();
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsDownload, procedure, AttachmentOf(HierarchyScenario.C1), downloadOn);

        // El estado y su cambio quedan en auditoría (public.audit_log por trigger de la tabla, HU #12323).
        var rows = await ctx.NetworkAccessAuditEntries.AsNoTracking().OrderBy(r => r.OccurredAt).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].Result.Should().Be(NetworkAccessVocabulary.Results.Forbidden);
        rows[1].Result.Should().Be(NetworkAccessVocabulary.Results.Ok);
        var updatedBy = await ctx.HierarchySwitches.AsNoTracking()
            .Where(s => s.SwitchKey == HierarchySwitch.NetworkDocumentsConcesionKey).Select(s => s.UpdatedBy).SingleAsync();
        updatedBy.Should().Be(HeadUser);
    }

    // ── AC6 — aislamiento ─────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_Hijo_a_hermano_cabeza_o_ajeno_y_cabeza_a_ajeno_o_inexistente_responden_como_un_recurso_inexistente()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        var storage = new InMemoryStorage();
        var handler = Handler(ctx, storage);
        var head = await ScopeOf(ctx, HierarchyScenario.P);
        var child = await ScopeOf(ctx, HierarchyScenario.C1);
        child.IsGroup.Should().BeFalse("un hijo no tiene red");

        // Usuario del hijo: la policy de cabeza lo detiene antes de cualquier consulta (mismo 403 que hoy
        // devuelve toda ruta /network a quien no es cabeza); jamás llega a distinguir hermano/cabeza/ajeno.
        foreach (var target in new[] { HierarchyScenario.C2, HierarchyScenario.P, HierarchyScenario.X })
        {
            var list = await handler.ListAsync(HierarchyScenario.DeliveredProcedureOf(target), child);
            var download = await handler.DownloadAsync(HierarchyScenario.DeliveredProcedureOf(target), AttachmentOf(target), child);
            list.Error.Should().Be(NetworkScopePolicy.ScopeRequired, HierarchyScenario.CodeOf(target));
            download.Error.Should().Be(NetworkScopePolicy.ScopeRequired, HierarchyScenario.CodeOf(target));
            list.ProcedureTenantId.Should().BeNull("ni siquiera se resuelve el dueño");
        }

        // Cabeza → ajeno, inexistente, documento inexistente y binario perdido: el MISMO error.
        var missingProcedure = Guid.NewGuid();
        var errors = new List<string?>
        {
            (await handler.ListAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X), head)).Error,
            (await handler.ListAsync(missingProcedure, head)).Error,
            (await handler.DownloadAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X), AttachmentOf(HierarchyScenario.X), head)).Error,
            (await handler.DownloadAsync(missingProcedure, AttachmentOf(HierarchyScenario.C1), head)).Error,
            (await handler.DownloadAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), Guid.NewGuid(), head)).Error,
        };
        storage.FailNext = true; // binario perdido en el almacenamiento
        errors.Add((await handler.DownloadAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), AttachmentOf(HierarchyScenario.C1), head)).Error);
        errors.Distinct().Should().ContainSingle().Which.Should().Be(NetworkDocumentsPolicy.NotFound);
        storage.Opened.Should().NotContain(p => p.Contains("/X/", StringComparison.Ordinal), "el binario del ajeno nunca se abre");

        // Conjunto de lectura vacío (o sin hijos): no hay red y no se devuelve ningún documento.
        var single = TenantScope.Group(HierarchyScenario.P, [], GroupKind.MarcaBlanca);
        single.IsGroup.Should().BeFalse();
        var nothing = await handler.ListAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), single);
        nothing.Result.Should().BeNull();
        nothing.Error.Should().Be(NetworkScopePolicy.ScopeRequired);
        (await handler.ListAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), null)).Error.Should().Be(NetworkScopePolicy.ScopeRequired);
    }

    // ── AC7 — interruptor de emergencia ───────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC7_Con_group_read_scope_apagado_la_cabeza_degrada_a_Single_y_recibe_network_scope_required()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        (await ScopeOf(ctx, HierarchyScenario.P)).IsGroup.Should().BeTrue();

        await new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance)
            .SetAsync(IHierarchySwitches.GroupReadScopeKey, false, HeadUser);

        var scope = await ScopeOf(ctx, HierarchyScenario.P);
        scope.IsGroup.Should().BeFalse("el resolver degrada sin tocar la jerarquía");
        var list = await Handler(ctx).ListAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), scope);
        var download = await Handler(ctx).DownloadAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), AttachmentOf(HierarchyScenario.C1), scope);
        list.Error.Should().Be(NetworkScopePolicy.ScopeRequired);
        download.Error.Should().Be(NetworkScopePolicy.ScopeRequired);
        NetworkAccessAuditPolicy.ReachedChildren(scope, [HierarchyScenario.C1], NetworkAccessVocabulary.Results.Forbidden).Should().BeEmpty("sin red no hay rastro");
        (await ctx.NetworkAccessAuditEntries.CountAsync()).Should().Be(0);
    }

    // ── AC8 — paridad de un cliente sin jerarquía ─────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_Un_cliente_sin_jerarquia_lista_y_descarga_por_los_handlers_de_hoy_con_el_mismo_resultado()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        var repo = new ProcedureInstanceRepository(ctx);
        var storage = new InMemoryStorage();
        var procedure = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.S);

        var (list, listError) = await new ListAttachmentsHandler(repo).HandleAsync(procedure, HierarchyScenario.S);
        var (download, downloadError) = await new DownloadAttachmentHandler(repo, storage).HandleAsync(procedure, HierarchyScenario.S, AttachmentOf(HierarchyScenario.S));

        listError.Should().BeNull();
        list!.Attachments.Should().ContainSingle().Which.Id.Should().Be(AttachmentOf(HierarchyScenario.S));
        downloadError.Should().BeNull();
        using var buffer = new MemoryStream();
        await download!.Content.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(BytesOf(HierarchyScenario.S));

        // Y con el tenant equivocado el resultado es el de siempre: not_found, sin fuga.
        (await new ListAttachmentsHandler(repo).HandleAsync(HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1), HierarchyScenario.S)).Error.Should().Be("not_found");
        (await ScopeOf(ctx, HierarchyScenario.S)).IsGroup.Should().BeFalse();
        (await ctx.NetworkAccessAuditEntries.CountAsync()).Should().Be(0, "las rutas viejas no escriben en la auditoría de red");
    }

    // ── AC4 / AC9 — auditoría: un registro por acceso, sobrevive al desvínculo ───────────────

    [PostgresFact]
    public async Task AC9_Escenario_completo_con_tres_clientes_deja_exactamente_un_registro_por_acceso_y_sobrevive_al_desvinculo()
    {
        await SeedAsync(headKind: GroupKindCodes.MarcaBlanca);
        await using var ctx = NewContext();
        var storage = new InMemoryStorage();
        var handler = Handler(ctx, storage);
        var scope = await ScopeOf(ctx, HierarchyScenario.P);
        var c1 = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var c2 = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2);
        var x = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.X);
        var own = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.P);

        // 1. listado C1 ok · 2. descarga C1 ok · 3. listado C2 ok · 4. descarga C2 documento inexistente (not_found)
        // 5. listado X (not_found imputado a X) · 6. listado propio (sin registro) · 7. inexistente (sin registro)
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsList, c1, null, await handler.ListAsync(c1, scope));
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsDownload, c1, AttachmentOf(HierarchyScenario.C1), await handler.DownloadAsync(c1, AttachmentOf(HierarchyScenario.C1), scope));
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsList, c2, null, await handler.ListAsync(c2, scope));
        var unknown = Guid.NewGuid();
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsDownload, c2, unknown, await handler.DownloadAsync(c2, unknown, scope));
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsList, x, null, await handler.ListAsync(x, scope));
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsList, own, null, await handler.ListAsync(own, scope));
        var missing = Guid.NewGuid();
        await RecordAsync(scope, NetworkAccessVocabulary.Resources.AttachmentsList, missing, null, await handler.ListAsync(missing, scope));

        var rows = await ctx.NetworkAccessAuditEntries.AsNoTracking().OrderBy(r => r.OccurredAt).ToListAsync();
        rows.Should().HaveCount(5, "un registro por acceso con hijo involucrado; propio e inexistente no dejan rastro");
        rows.Should().OnlyContain(r => r.ActorTenantId == HierarchyScenario.P && r.ActorUserId == HeadUser);
        rows[0].Should().Match<Flit.Infrastructure.Persistence.Entities.Tramites.NetworkAccessAuditEntry>(r =>
            r.Resource == NetworkAccessVocabulary.Resources.AttachmentsList && r.Result == "ok" && r.ProcedureId == c1 && r.ProcedureTenantId == HierarchyScenario.C1 && r.AttachmentId == null);
        rows[1].Should().Match<Flit.Infrastructure.Persistence.Entities.Tramites.NetworkAccessAuditEntry>(r =>
            r.Resource == NetworkAccessVocabulary.Resources.AttachmentsDownload && r.Result == "ok" && r.AttachmentId == AttachmentOf(HierarchyScenario.C1));
        rows[2].ProcedureTenantId.Should().Be(HierarchyScenario.C2);
        rows[3].Should().Match<Flit.Infrastructure.Persistence.Entities.Tramites.NetworkAccessAuditEntry>(r =>
            r.Result == "not_found" && r.AttachmentId == unknown && r.ProcedureTenantId == HierarchyScenario.C2);
        rows[4].Should().Match<Flit.Infrastructure.Persistence.Entities.Tramites.NetworkAccessAuditEntry>(r =>
            r.Result == "not_found" && r.ProcedureId == x && r.ProcedureTenantId == HierarchyScenario.X);
        rows.Should().NotContain(r => r.ProcedureId == own || r.ProcedureId == missing);

        // Cada hijo ve en SU auditoría solo lo que lo alcanzó; el ajeno ve el intento rechazado sobre él.
        var reader = new NetworkAccessAuditReader(ctx);
        (await reader.SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C1, null, null, null))).Total.Should().Be(2);
        (await reader.SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C2, null, null, null))).Total.Should().Be(2);
        (await reader.SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.X, null, null, null))).Total.Should().Be(1);

        // AC4 — el desvínculo de C1 no borra ni oculta sus registros, y P deja de alcanzarlo.
        await using (var admin = NewContext())
        {
            var tenant = await admin.Tenants.SingleAsync(t => t.Id == HierarchyScenario.C1);
            tenant.ParentTenantId = null;
            await admin.SaveChangesAsync();
        }
        await using var after = NewContext();
        (await new NetworkAccessAuditReader(after).SearchAsync(new NetworkAccessAuditQuery(HierarchyScenario.C1, null, null, null))).Total.Should().Be(2);
        var narrowed = await ScopeOf(after, HierarchyScenario.P);
        narrowed.ReadTenantIds.Should().NotContain(HierarchyScenario.C1);
        (await Handler(after, storage).ListAsync(c1, narrowed)).Error.Should().Be(NetworkDocumentsPolicy.NotFound, "tras el desvínculo C1 es un ajeno más");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Escenario canónico + un anexo por trámite entregado; la cabeza P con la clase pedida.</summary>
    private async Task SeedAsync(string headKind)
    {
        await HierarchyScenario.SeedAsync(Fixture, headTenantType: headKind);
        await using var ctx = NewContext();
        foreach (var tenant in HierarchyScenario.Clients)
        {
            var code = HierarchyScenario.CodeOf(tenant);
            ctx.ProcedureInstanceAttachments.Add(new ProcedureInstanceAttachment
            {
                Id = AttachmentOf(tenant),
                TenantId = tenant,
                ProcedureInstanceId = HierarchyScenario.DeliveredProcedureOf(tenant),
                Tipo = "factura",
                Filename = $"factura-{code}.pdf",
                Mimetype = "application/pdf",
                SizeBytes = BytesOf(tenant).Length,
                Sha256 = $"sha-{code}",
                StoragePath = $"store/{code}/factura.pdf",
                Source = "user",
                UploadedAt = DateTimeOffset.UtcNow.AddHours(-1),
                UploadedBy = HierarchyScenario.UserOf(tenant),
            });
        }
        await ctx.SaveChangesAsync();
    }

    private static Task<TenantScope> ScopeOf(FlitDbContext ctx, Guid tenantId) =>
        new DbTenantScopeResolver(ctx, new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance), NullLogger<DbTenantScopeResolver>.Instance)
            .ResolveAsync(tenantId);

    private static NetworkAttachmentsHandler Handler(FlitDbContext ctx, InMemoryStorage? storage = null)
    {
        var repo = new ProcedureInstanceRepository(ctx);
        return new NetworkAttachmentsHandler(
            new ProcedureInstanceOwnerLookup(ctx),
            new DbHierarchySwitches(ctx, NullLogger<DbHierarchySwitches>.Instance),
            new ListAttachmentsHandler(repo),
            new DownloadAttachmentHandler(repo, storage ?? new InMemoryStorage()));
    }

    /// <summary>
    /// Lo que hace <c>NetworkAttachmentEndpoints.Publish</c> + <c>NetworkAccessAuditFilter</c> tras cada
    /// petición: con dueño resuelto publica ok/forbidden/not_found imputado a ese tenant; el filtro
    /// descarta a la cabeza y, si no queda hijo, no escribe.
    /// </summary>
    private async Task RecordAsync<T>(TenantScope scope, string resource, Guid procedureId, Guid? attachmentId, NetworkAttachmentsOutcome<T> outcome)
        where T : class
    {
        if (outcome.ProcedureTenantId is not { } owner)
            return;

        var result = outcome.Error switch
        {
            null => NetworkAccessVocabulary.Results.Ok,
            NetworkDocumentsPolicy.NotFound => NetworkAccessVocabulary.Results.NotFound,
            _ => NetworkAccessVocabulary.Results.Forbidden,
        };
        var reached = NetworkAccessAuditPolicy.ReachedChildren(scope, [owner], result);
        if (reached.Count == 0)
            return;

        await Writer().WriteAsync(new NetworkAccessAuditEntry(
            HeadUser, scope.WriteTenantId!.Value, reached, resource, null, procedureId, owner, attachmentId, result));
    }

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

    /// <summary>Almacenamiento en memoria: el binario de cada cliente por su <c>storagePath</c>; registra qué se abrió.</summary>
    private sealed class InMemoryStorage : IAttachmentStorage
    {
        private static readonly Dictionary<string, byte[]> Files = HierarchyScenario.Clients
            .ToDictionary(t => $"store/{HierarchyScenario.CodeOf(t)}/factura.pdf", BytesOf, StringComparer.Ordinal);

        public List<string> Opened { get; } = [];

        public bool FailNext { get; set; }

        public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default)
        {
            if (FailNext)
            {
                FailNext = false;
                return Task.FromResult<Stream?>(null);
            }

            Opened.Add(storagePath);
            return Task.FromResult<Stream?>(Files.TryGetValue(storagePath, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        }

        public Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default) =>
            throw new NotSupportedException("solo lectura");

        public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
            throw new NotSupportedException("solo lectura");

        public void Delete(string storagePath) => throw new NotSupportedException("solo lectura");

        public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
            throw new NotSupportedException("la red no expone direcciones prefirmadas");
    }
}
