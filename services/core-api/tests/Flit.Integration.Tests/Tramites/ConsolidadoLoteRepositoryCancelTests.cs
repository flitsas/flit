using System.Diagnostics;
using System.Net;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13385 (Feature #13307, diseño 09 §2.4) — <see cref="ConsolidadoLoteRepository.CancelarAsync"/> contra PostgreSQL real
/// con los CHECK de los DDL 133/134:
/// <list type="bullet">
///   <item>AC1/AC4: lote <c>en_proceso</c> con una parte cerrada y otra empaquetando → <c>cancelado</c>, DEK NULL,
///   <c>finished_at = expires_at = purged_at</c>, ítems vivos <c>cancelado</c>, parte cerrada <c>purgada</c>, la otra
///   <c>descartada</c> y una sola fila <c>lote_cancelado</c> (con conteos, <c>parts_count</c> NULL). La segunda petición es
///   idempotente. Después, <c>actual</c> no lo devuelve (purgado ⇒ 204) y por id sale <c>cancelado</c> sin partes.</item>
///   <item>AC2: el índice único parcial libera el cupo: antes de cancelar la creación da <c>LoteActivo</c>, después
///   <c>Creado</c>, y la creación no vuelve a purgar el cancelado.</item>
///   <item>AC3: los consolidados generados por el lote siguen en el trámite.</item>
///   <item>AC5/AC6: terminal → <c>Terminado</c> sin cambios; otro usuario o id inexistente → <c>NoEncontrado</c>.</item>
///   <item>AC8: si el INSERT de <c>lote_cancelado</c> falla, rollback completo y el lote sigue activo.</item>
///   <item>AC9: lote del Super Admin → <c>actor_tenant_id</c> NULL y <c>reached_tenant_ids</c> = {A, B}.</item>
///   <item>R-7: 20.000 ítems vivos se cancelan en una transacción; se mide el tiempo.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await using var ctx = NewContext();
/// var r = await new ConsolidadoLoteRepository(ctx).CancelarAsync(new CancelacionLote(lote, sub, "Radicador"), ct);
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteRepositoryCancelTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid CompaniaA = new("b13385a0-0000-7000-8000-000000013385");
    private static readonly Guid CompaniaB = new("b13385b0-0000-7000-8000-000000013385");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ── Siembra ─────────────────────────────────────────────────────────────────────────────────────────

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn) { CommandTimeout = 120 };
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private async Task SembrarCompaniasAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(CompaniaA, "IT-CIA-13385A", false, null));
        ctx.Tenants.Add(TenantSeed.New(CompaniaB, "IT-CIA-13385B", false, null));
        await ctx.SaveChangesAsync(Ct);
    }

    private async Task<Guid> UsuarioAsync(string sufijo)
    {
        var u = Guid.CreateVersion7();
        await ExecAsync(
            "INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES (@u, @e, 'Usuario lote', 'active', now())",
            ("u", u), ("e", $"lote13385-{sufijo}@it.test"));
        return u;
    }

    /// <summary><paramref name="n"/> trámites de la compañía con una secuencia propia (radicado y VIN sintéticos).</summary>
    private async Task<List<Guid>> TramitesAsync(Guid compania, Guid usuario, int n, string prefijo)
    {
        var ids = Enumerable.Range(0, n).Select(_ => Guid.CreateVersion7()).ToArray();
        await ExecAsync(
            """
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            SELECT t.id, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                   @p || '-' || t.n, 'borrador', 'VIN' || @p || lpad(t.n::text, 8, '0'), @u, now()
              FROM unnest(@ids) WITH ORDINALITY AS t(id, n)
            """,
            ("c", compania), ("u", usuario), ("p", prefijo), ("ids", ids));
        return [.. ids];
    }

    /// <summary>Lote activo (o terminal) del usuario. Super Admin ⇒ sin compañía (Q8).</summary>
    private async Task<Guid> LoteAsync(
        Guid usuario, string estado = ConsolidadoExportStatus.EnProceso, int total = 0, int incluidos = 0,
        int generados = 0, bool superAdmin = false)
    {
        var lote = Guid.CreateVersion7();
        var terminal = !ConsolidadoExportStatus.EsActivo(estado);
        await ExecAsync(
            $"""
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, included_count, generated_count, dek_wrapped, effects_acknowledged_at, created_by, started_at,
               finished_at, expires_at)
            VALUES (@l, {(superAdmin ? "NULL" : "@c")}, @u, @rol, @origen, 'consolidado', 'ids', @s, @t, @i, @g,
                    '\x010203'::bytea, now(), @u, now(),
                    {(terminal ? "now(), now() + interval '23 hours'" : "NULL, NULL")});
            """,
            ("l", lote), ("c", CompaniaA), ("u", usuario), ("rol", superAdmin ? "SuperAdmin" : "Radicador"),
            ("origen", superAdmin ? ConsolidadoExportOrigin.Superadmin : ConsolidadoExportOrigin.Tramites),
            ("s", estado), ("t", total), ("i", incluidos), ("g", generados));
        return lote;
    }

    /// <summary>Ítems por SQL: <c>pendiente</c>, <c>procesando</c> (con lease) o <c>incluido</c> (snapshot + parte).</summary>
    private async Task ItemsAsync(
        Guid lote, Guid usuario, IReadOnlyList<Guid> tramites, Guid compania, string estado, int desde = 0,
        short? parte = null, IReadOnlyList<Guid>? adjuntos = null)
    {
        await ExecAsync(
            """
            INSERT INTO tramites.consolidado_export_batch_items
                (tenant_id, batch_id, procedure_instance_id, position, status, lease_until, claimed_by, reference_number,
                 attachment_id, storage_path, size_bytes, delivery_mode, processed_at, part_number, created_by)
            SELECT @c, @l, t.id, @desde + t.n - 1, @s,
                   CASE WHEN @s = 'procesando' THEN now() + interval '10 minutes' END,
                   CASE WHEN @s = 'procesando' THEN 'slot-1' END,
                   'IT-13385-' || (@desde + t.n),
                   CASE WHEN @s = 'incluido' THEN COALESCE(a.id, gen_random_uuid()) END,
                   CASE WHEN @s = 'incluido' THEN 'fm/consolidado-' || t.n END,
                   CASE WHEN @s = 'incluido' THEN 100 END,
                   CASE WHEN @s = 'incluido' THEN 'generado' END,
                   CASE WHEN @s = 'incluido' THEN now() END,
                   CASE WHEN @s = 'incluido' THEN NULLIF(@parte, 0::smallint) END,
                   @u
              FROM unnest(@ids) WITH ORDINALITY AS t(id, n)
              LEFT JOIN unnest(@adj) WITH ORDINALITY AS a(id, n) ON a.n = t.n
            """,
            ("c", compania), ("l", lote), ("u", usuario), ("s", estado), ("desde", desde),
            ("parte", parte ?? (short)0), ("ids", tramites.ToArray()),
            ("adj", (adjuntos ?? []).ToArray()));
    }

    private async Task ParteAsync(Guid lote, short numero, string estado)
    {
        var cerrada = estado == ConsolidadoExportPartStatus.Cerrada;
        await ExecAsync(
            $"""
            INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, status, pdf_count, lease_until,
                storage_path, stored_sha256, stored_size_bytes, plain_size_bytes, closed_at)
            VALUES (@l, @n, @s, 1, {(estado == ConsolidadoExportPartStatus.Empaquetando ? "now() + interval '5 minutes'" : "NULL")},
                    {(cerrada ? "'fm/parte-' || @n, repeat('b', 64), 110, 100, now()" : "NULL, NULL, NULL, NULL, NULL")})
            """,
            ("l", lote), ("n", numero), ("s", estado));
    }

    private Task<long> AuditoriasAsync(Guid lote, string evento) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = @e", ("l", lote), ("e", evento));

    private Task<long> ItemsEnAsync(Guid lote, string estado) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND status = @s", ("l", lote), ("s", estado));

    private Task<string> EstadoParteAsync(Guid lote, short n) => ScalarAsync<string>(
        "SELECT status FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND part_number = @n", ("l", lote), ("n", n));

    private async Task<CancelarLoteResultado> CancelarAsync(Guid lote, Guid usuario, string rol = "Radicador")
    {
        await using var ctx = NewContext();
        return await new ConsolidadoLoteRepository(ctx).CancelarAsync(
            new CancelacionLote(lote, usuario, rol, IPAddress.Parse("10.13.38.5"), "ua/13385"), Ct);
    }

    /// <summary>Lote <c>en_proceso</c>: 2 incluidos (parte 1 cerrada, parte 2 empaquetando), 2 pendientes y 1 procesando.</summary>
    private async Task<(Guid Usuario, Guid Lote)> EscenarioAc1Async()
    {
        await SembrarCompaniasAsync();
        var u = await UsuarioAsync("ac1");
        var tramites = await TramitesAsync(CompaniaA, u, 5, "AC1");
        var lote = await LoteAsync(u, total: 5, incluidos: 2, generados: 1);
        await ParteAsync(lote, 1, ConsolidadoExportPartStatus.Cerrada);
        await ParteAsync(lote, 2, ConsolidadoExportPartStatus.Empaquetando);
        await ItemsAsync(lote, u, tramites[..1], CompaniaA, ConsolidadoExportItemStatus.Incluido, 0, parte: 1);
        await ItemsAsync(lote, u, tramites[1..2], CompaniaA, ConsolidadoExportItemStatus.Incluido, 1, parte: 2);
        await ItemsAsync(lote, u, tramites[2..4], CompaniaA, ConsolidadoExportItemStatus.Pendiente, 2);
        await ItemsAsync(lote, u, tramites[4..], CompaniaA, ConsolidadoExportItemStatus.Procesando, 4);
        return (u, lote);
    }

    // ── AC1 + AC4 ──────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_AC4_CancelaElLoteActivo_ItemsPartesDekEInstantes_UnaAuditoria_EIdempotente()
    {
        var (u, lote) = await EscenarioAc1Async();

        var r = await CancelarAsync(lote, u);

        r.Estado.Should().Be(CancelarLoteEstado.Cancelado);
        r.ItemsCancelados.Should().Be(3, "2 pendientes + 1 procesando");
        r.RutasPartesPurgadas.Should().Equal("fm/parte-1");
        r.Lote!.Status.Should().Be(ConsolidadoExportStatus.Cancelado);

        await using (var cn = await Fixture.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT status, dek_wrapped IS NULL, finished_at = expires_at AND expires_at = purged_at, finished_at,
                   total_items, included_count, omitted_count, generated_count, updated_by
              FROM tramites.consolidado_export_batches WHERE id = @l
            """, cn))
        {
            cmd.Parameters.AddWithValue("l", lote);
            await using var rd = await cmd.ExecuteReaderAsync(Ct);
            (await rd.ReadAsync(Ct)).Should().BeTrue();
            rd.GetString(0).Should().Be(ConsolidadoExportStatus.Cancelado);
            rd.GetBoolean(1).Should().BeTrue("borrado criptográfico de la DEK");
            rd.GetBoolean(2).Should().BeTrue("finished_at, expires_at y purged_at = instante de la cancelación");
            rd.GetFieldValue<DateTimeOffset>(3).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
            (rd.GetInt32(4), rd.GetInt32(5), rd.GetInt32(6), rd.GetInt32(7)).Should().Be((5, 2, 0, 1), "contadores como estaban");
            rd.GetGuid(8).Should().Be(u);
        }

        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Cancelado)).Should().Be(3);
        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Incluido)).Should().Be(2, "lo ya procesado no cambia");
        (await ScalarAsync<long>(
                "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND lease_until IS NOT NULL",
                ("l", lote)))
            .Should().Be(0);
        (await EstadoParteAsync(lote, 1)).Should().Be(ConsolidadoExportPartStatus.Purgada);
        (await EstadoParteAsync(lote, 2)).Should().Be(ConsolidadoExportPartStatus.Descartada);

        await using (var ctx = NewContext())
        {
            var audit = await ctx.ConsolidadoExportAuditEntries.AsNoTracking()
                .SingleAsync(a => a.BatchId == lote && a.Event == ConsolidadoExportAuditEvent.LoteCancelado, Ct);
            audit.ActorUserId.Should().Be(u);
            audit.ActorTenantId.Should().Be(CompaniaA);
            audit.ActorRoleCode.Should().Be("Radicador");
            (audit.TotalItems, audit.IncludedCount, audit.OmittedCount, audit.GeneratedCount).Should().Be((5, 2, 0, 1));
            audit.PartsCount.Should().BeNull("ck_consolidado_export_audit_parts: solo lote_finalizado lleva parts_count");
            audit.ReachedTenantIds.Should().Equal(CompaniaA);
            audit.ClientIp!.ToString().Should().Be("10.13.38.5");
            audit.OccurredAt.Should().BeCloseTo(r.Lote.FinishedAt!.Value, TimeSpan.FromMilliseconds(1));
        }

        // AC4 — doble cancelación: 202 tal cual, sin otra auditoría.
        var otra = await CancelarAsync(lote, u);
        otra.Estado.Should().Be(CancelarLoteEstado.YaCancelado);
        otra.Lote!.Status.Should().Be(ConsolidadoExportStatus.Cancelado);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteCancelado)).Should().Be(1);

        // Consulta tras cancelar (#13379 tal cual): purgado ⇒ «actual» vacío (204); por id, cancelado sin partes; descarga 404.
        await using (var ctx = NewContext())
        {
            var lectura = new ConsolidadoLoteLectura(ctx);
            (await lectura.ObtenerActualDelDuenoAsync(u, Ct)).Should().BeNull("purged_at informado ⇒ 204 hasta #13386");
            var porId = await new ConsultarLoteConsolidadosHandler(lectura).PorIdAsync(new ObtenerLoteQuery(lote, u), Ct);
            porId!.Lote.Status.Should().Be(ConsolidadoExportStatus.Cancelado);
            porId.Partes.Should().BeEmpty();
            var descarga = await new DescargarParteHandler(lectura, Substitute.For<IConsolidadoLoteParteStorage>(),
                    Substitute.For<IConsolidadoLoteCipher>())
                .PrepararAsync(new DescargarParteQuery(lote, 1, u, "Radicador"), Ct);
            descarga.Estado.Should().Be(DescargarParteEstado.NoEncontrada);
        }
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_ElIndiceUnicoLiberaElCupo_LaCreacionPasaDeLoteActivoACreado_YNoRepurgaElCancelado()
    {
        await SembrarCompaniasAsync();
        var u = await UsuarioAsync("ac2");
        var tramites = await TramitesAsync(CompaniaA, u, 2, "AC2");
        var lote = await LoteAsync(u, total: 1);
        await ItemsAsync(lote, u, tramites[..1], CompaniaA, ConsolidadoExportItemStatus.Pendiente);

        (await CrearAsync(u, tramites[1])).Estado.Should().Be(CrearLoteEstado.LoteActivo, "el cupo está ocupado");

        (await CancelarAsync(lote, u)).Estado.Should().Be(CancelarLoteEstado.Cancelado);
        var nuevo = await CrearAsync(u, tramites[1]);

        nuevo.Estado.Should().Be(CrearLoteEstado.Creado, "cancelado sale de uq_consolidado_export_batches_active_per_user");
        nuevo.LotesPurgados.Should().Be(0, "el cancelado ya tiene purged_at");
        (await ScalarAsync<string>("SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote)))
            .Should().Be(ConsolidadoExportStatus.Cancelado);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LotePurgado)).Should().Be(0);
    }

    private async Task<CrearLoteResultado> CrearAsync(Guid usuario, Guid tramite)
    {
        await using var ctx = NewContext();
        return await new ConsolidadoLoteRepository(ctx).CrearAsync(new NuevoLoteConsolidados
        {
            TenantId = CompaniaA,
            UsuarioId = usuario,
            RolCodigo = "Radicador",
            Origen = ConsolidadoExportOrigin.Tramites,
            TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
            ModoSeleccion = ConsolidadoExportSelectionMode.Ids,
            DekEnvuelta = [1, 2, 3],
            EfectosAceptadosEn = DateTimeOffset.UtcNow,
            Items = [new ProcedureInstanceRef(tramite, CompaniaA, "R-13385", null)],
            IdsCount = 1,
        }, Ct);
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_LosConsolidadosQueElLoteGeneroSiguenOficialesEnSusTramites()
    {
        await SembrarCompaniasAsync();
        var u = await UsuarioAsync("ac3");
        var tramites = await TramitesAsync(CompaniaA, u, 4, "AC3");
        var adjuntos = new List<Guid>();
        foreach (var t in tramites[..3])
        {
            var id = Guid.CreateVersion7();
            await ExecAsync(
                """
                INSERT INTO tramites.procedure_instance_attachments
                    (id, tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at)
                VALUES (@id, @c, @t, 'consolidado', 'consolidado.pdf', 'application/pdf', 100, repeat('c', 64), 'fm/gen-' || @id, now())
                """,
                ("id", id), ("c", CompaniaA), ("t", t));
            adjuntos.Add(id);
        }

        var lote = await LoteAsync(u, total: 4, incluidos: 3, generados: 3);
        await ParteAsync(lote, 1, ConsolidadoExportPartStatus.Pendiente);
        await ItemsAsync(lote, u, tramites[..3], CompaniaA, ConsolidadoExportItemStatus.Incluido, 0, parte: 1, adjuntos: adjuntos);
        await ItemsAsync(lote, u, tramites[3..], CompaniaA, ConsolidadoExportItemStatus.Pendiente, 3);

        (await CancelarAsync(lote, u)).Estado.Should().Be(CancelarLoteEstado.Cancelado);

        (await ScalarAsync<long>(
                """
                SELECT count(*) FROM tramites.procedure_instance_attachments
                 WHERE tipo = 'consolidado' AND id = ANY(@a) AND procedure_instance_id = ANY(@t)
                """,
                ("a", adjuntos.ToArray()), ("t", tramites[..3].ToArray())))
            .Should().Be(3, "la cancelación no revierte los consolidados ya generados (CF-09)");
        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Incluido)).Should().Be(3);
        (await ScalarAsync<int>("SELECT generated_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote)))
            .Should().Be(3);
        (await EstadoParteAsync(lote, 1)).Should().Be(ConsolidadoExportPartStatus.Descartada);
    }

    // ── AC5 + AC6 ───────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_AC6_TerminadoNoCambia_YOtroUsuarioOIdInexistenteNoEncuentran()
    {
        await SembrarCompaniasAsync();
        var u = await UsuarioAsync("ac5");
        var otro = await UsuarioAsync("ac6-otro");
        var completado = await LoteAsync(u, ConsolidadoExportStatus.Completado);
        var activo = await LoteAsync(otro);

        var r = await CancelarAsync(completado, u);
        r.Estado.Should().Be(CancelarLoteEstado.Terminado);
        r.Lote!.Status.Should().Be(ConsolidadoExportStatus.Completado);
        (await ScalarAsync<bool>(
                "SELECT status = 'completado' AND dek_wrapped IS NOT NULL AND purged_at IS NULL FROM tramites.consolidado_export_batches WHERE id = @l",
                ("l", completado)))
            .Should().BeTrue();

        (await CancelarAsync(activo, u, "SuperAdmin")).Estado.Should().Be(CancelarLoteEstado.NoEncontrado, "lote ajeno ⇒ 404");
        (await CancelarAsync(Guid.CreateVersion7(), u)).Estado.Should().Be(CancelarLoteEstado.NoEncontrado);
        (await ScalarAsync<string>("SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", activo)))
            .Should().Be(ConsolidadoExportStatus.EnProceso);
        (await ScalarAsync<long>("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
    }

    // ── AC8 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_SiFallaLaAuditoria_RollbackCompleto_YElLoteSigueActivo()
    {
        var (u, lote) = await EscenarioAc1Async();
        await ExecAsync(
            """
            CREATE OR REPLACE FUNCTION public.tmp_falla_auditoria_13385() RETURNS trigger AS $$
            BEGIN
                IF NEW.event = 'lote_cancelado' THEN RAISE EXCEPTION 'auditoria no disponible (prueba HU13385)'; END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER tmp_falla_auditoria_13385 BEFORE INSERT ON tramites.consolidado_export_audit
                FOR EACH ROW EXECUTE FUNCTION public.tmp_falla_auditoria_13385();
            """);

        CancelarLoteResultado r;
        try
        {
            r = await CancelarAsync(lote, u);
        }
        finally
        {
            await ExecAsync(
                """
                DROP TRIGGER IF EXISTS tmp_falla_auditoria_13385 ON tramites.consolidado_export_audit;
                DROP FUNCTION IF EXISTS public.tmp_falla_auditoria_13385();
                """);
        }

        r.Estado.Should().Be(CancelarLoteEstado.NoRegistrado);
        (await ScalarAsync<bool>(
                "SELECT status = 'en_proceso' AND dek_wrapped IS NOT NULL AND finished_at IS NULL AND purged_at IS NULL FROM tramites.consolidado_export_batches WHERE id = @l",
                ("l", lote)))
            .Should().BeTrue("el lote sigue activo y con su DEK");
        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Pendiente)).Should().Be(2);
        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Procesando)).Should().Be(1);
        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Cancelado)).Should().Be(0);
        (await EstadoParteAsync(lote, 1)).Should().Be(ConsolidadoExportPartStatus.Cerrada);
        (await EstadoParteAsync(lote, 2)).Should().Be(ConsolidadoExportPartStatus.Empaquetando);
        (await AuditoriasAsync(lote, ConsolidadoExportAuditEvent.LoteCancelado)).Should().Be(0);
    }

    // ── AC9 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC9_LoteDelSuperAdmin_AuditaSinCompania_YConLasCompaniasAlcanzadas()
    {
        await SembrarCompaniasAsync();
        var sa = await UsuarioAsync("ac9-sa");
        var deA = await TramitesAsync(CompaniaA, sa, 2, "AC9A");
        var deB = await TramitesAsync(CompaniaB, sa, 1, "AC9B");
        var lote = await LoteAsync(sa, total: 3, superAdmin: true);
        await ItemsAsync(lote, sa, deA, CompaniaA, ConsolidadoExportItemStatus.Pendiente);
        await ItemsAsync(lote, sa, deB, CompaniaB, ConsolidadoExportItemStatus.Pendiente, 2);

        (await CancelarAsync(lote, sa, "SuperAdmin")).Estado.Should().Be(CancelarLoteEstado.Cancelado);

        await using var ctx = NewContext();
        var audit = await ctx.ConsolidadoExportAuditEntries.AsNoTracking()
            .SingleAsync(a => a.BatchId == lote && a.Event == ConsolidadoExportAuditEvent.LoteCancelado, Ct);
        audit.Origin.Should().Be(ConsolidadoExportOrigin.Superadmin);
        audit.ActorTenantId.Should().BeNull();
        audit.ActorRoleCode.Should().Be("SuperAdmin");
        audit.ReachedTenantIds.Should().BeEquivalentTo([CompaniaA, CompaniaB]);
        audit.PartsCount.Should().BeNull();
    }

    // ── R-7 ─────────────────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task R7_Cancela20000ItemsVivosEnUnaTransaccion_YMideElTiempo()
    {
        const int n = 20_000;
        await SembrarCompaniasAsync();
        var u = await UsuarioAsync("r7");
        var tramites = await TramitesAsync(CompaniaA, u, n, "R7");
        var lote = await LoteAsync(u, total: n);
        await ItemsAsync(lote, u, tramites, CompaniaA, ConsolidadoExportItemStatus.Pendiente);

        var reloj = Stopwatch.StartNew();
        var r = await CancelarAsync(lote, u);
        reloj.Stop();

        r.Estado.Should().Be(CancelarLoteEstado.Cancelado);
        r.ItemsCancelados.Should().Be(n);
        (await ItemsEnAsync(lote, ConsolidadoExportItemStatus.Cancelado)).Should().Be(n);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"R-7: cancelación de {n} ítems vivos en {reloj.ElapsedMilliseconds} ms (una transacción).");
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "cota holgada anti-cuelgue; el umbral de R-7 (2 s) se reporta");
    }
}
