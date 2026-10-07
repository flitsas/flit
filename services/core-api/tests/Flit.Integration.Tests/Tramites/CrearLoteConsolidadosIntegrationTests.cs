using System.Diagnostics;
using System.Text.Json;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13373 (Feature #13306, épica #13216, ADR-0070 D8) — creación del lote contra PostgreSQL real:
/// <see cref="CrearLoteConsolidadosHandler"/> + <see cref="ConsolidadoLoteRepository"/> + el resolver real del listado de
/// <c>/tramites</c> (<see cref="TramitesSeleccionResolver"/>) y el cifrado real de la DEK. Cubre la transacción única
/// (lote + ítems por COPY + auditoría), el 23505 del índice único parcial, el fallo de la auditoría, la purga del lote
/// retenido, el tenant del token, el rendimiento con 20.000 ítems y el motor apagado. Escenario base:
/// <see cref="HierarchyScenario"/> (compañías C1 y C2 con su usuario).
/// <para>Uso de ejemplo:
/// <code>
/// var r = await Handler(ctx).HandleAsync(new CrearLoteConsolidadosCommand { Origen = "tramites", TenantId = C1, ... }, ct);
/// </code></para>
/// </summary>
public sealed class CrearLoteConsolidadosIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid C1 = HierarchyScenario.C1;
    private static readonly Guid C2 = HierarchyScenario.C2;
    private static readonly Guid Usuario = HierarchyScenario.UserOf(HierarchyScenario.C1);

    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CrearLoteConsolidadosHandler Handler(FlitDbContext ctx)
    {
        var procedimientos = new ProcedureInstanceRepository(ctx);
        return new CrearLoteConsolidadosHandler(
            new ConsolidadoLoteRepository(ctx),
            new LoteSeleccionResolverPorOrigen(
                [new TramitesSeleccionResolver(new ListProcedureInstancesFilteredHandler(procedimientos), procedimientos)]),
            new ConsolidadoLoteCipher(_dataProtection));
    }

    private static CrearLoteConsolidadosCommand Gestor(LoteSeleccion seleccion) => new()
    {
        Origen = ConsolidadoExportOrigin.Tramites,
        TenantId = C1,
        UsuarioId = Usuario,
        RolCodigo = "Radicador",
        TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
        Seleccion = seleccion,
        ConfirmaEfectos = true,
        ClientIp = System.Net.IPAddress.Parse("10.1.2.3"),
        UserAgent = "pruebas-13373",
    };

    /// <summary>
    /// «Seleccionar todos» del listado filtrado por estado <c>preparado</c>: el escenario base solo siembra
    /// <c>entregado</c> y <c>borrador</c>, así que el filtro abarca exactamente los trámites de la prueba. (El filtro
    /// suelto de placa del listado es por igualdad, no por prefijo.)
    /// </summary>
    private static SeleccionPorFiltro Preparados(IReadOnlyList<Guid>? excluidos = null) =>
        new(new TramitesLoteFiltro(new ProcedureInstanceListRequest { Estados = [TramiteEstado.Preparado] }), excluidos);

    /// <summary>El reset del arnés vacía la tabla de parámetros: cada prueba la siembra.</summary>
    /// <remarks>M1: <paramref name="tope"/> <c>null</c> = el DEFAULT de la columna <c>max_items_per_batch</c> (10.000).</remarks>
    private async Task SembrarSettingsAsync(bool activo = true, int? tope = null)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active) VALUES (uuidv7(), @a);
            """, ("a", activo));
        if (tope is { } t)
            await ExecAsync(cn, "UPDATE tramites.consolidado_export_settings SET max_items_per_batch = @t", ("t", t));
    }

    private async Task<IReadOnlyList<Guid>> SembrarTramitesAsync(Guid tenant, int cuantos, string prefijoPlaca)
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
                ReferenceNumber = $"{600000 + i}",
                Status = TramiteEstado.Preparado,
                Plate = $"{prefijoPlaca}{i:D5}",
                Vin = $"VINL13373{i:D8}",
                CreatedByUserId = HierarchyScenario.UserOf(tenant),
                CreatedAt = now.AddMinutes(-i - 10),
            };
            ctx.ProcedureInstances.Add(p);
            ids.Add(p.Id);
        }

        await ctx.SaveChangesAsync(Ct);
        return ids;
    }

    /// <summary>Lote terminal del usuario (completado, sin expirar) con una parte cerrada y un ítem incluido con placa.</summary>
    private async Task<Guid> SembrarLoteTerminalAsync(Guid tramite)
    {
        var lote = Guid.CreateVersion7();
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, included_count, dek_wrapped, effects_acknowledged_at, finished_at, expires_at, created_by)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'completado', 1, 1, '\x0a0b0c'::bytea,
                    now() - interval '2 hours', now() - interval '1 hour', now() + interval '23 hours', @u);
            INSERT INTO tramites.consolidado_export_batch_parts
              (batch_id, part_number, status, pdf_count, plain_size_bytes, stored_size_bytes, stored_sha256, storage_path,
               closed_at)
            VALUES (@l, 1, 'cerrada', 1, 10, 40, repeat('a', 64), 'obj-13373', now() - interval '1 hour');
            INSERT INTO tramites.consolidado_export_batch_items
              (tenant_id, batch_id, procedure_instance_id, position, status, reference_number, plate, attachment_id,
               storage_path, size_bytes, delivery_mode, part_number, processed_at, created_by)
            VALUES (@c, @l, @t, 0, 'incluido', 'R-1', 'PLT001', uuidv7(), 'pdf-1', 10, 'existente', 1, now(), @u);
            """, ("l", lote), ("c", C1), ("u", Usuario), ("t", tramite));
        return lote;
    }

    private async Task<Guid> SembrarLoteActivoAsync()
    {
        var lote = Guid.CreateVersion7();
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, created_by)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'en_proceso', 1, '\x0a'::bytea, now(), @u);
            """, ("l", lote), ("c", C1), ("u", Usuario));
        return lote;
    }

    private async Task<long> ContarAsync(string sql, params (string Name, object? Value)[] args)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        return await ScalarAsync<long>(cn, sql, args);
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Filtro350MenosDos_CreaLoteEnColaCon348ItemsEnOrden_AuditoriaYDekEnvuelta()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var ids = await SembrarTramitesAsync(C1, 350, "QZLT");
        var excluidos = new[] { ids[5], ids[300] };

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(Preparados(excluidos)), Ct);

        r.Creado.Should().BeTrue(r.Error);
        var loteId = r.Lote!.Id;
        await using var db = NewContext();
        var lote = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == loteId, Ct);
        lote.Status.Should().Be(ConsolidadoExportStatus.EnCola);
        lote.TotalItems.Should().Be(348);
        lote.TenantId.Should().Be(C1);
        lote.SelectionMode.Should().Be(ConsolidadoExportSelectionMode.Filtro);

        var items = await db.ConsolidadoExportBatchItems.AsNoTracking()
            .Where(i => i.BatchId == loteId).OrderBy(i => i.Position).ToListAsync(Ct);
        items.Should().HaveCount(348).And.OnlyContain(i => i.Status == ConsolidadoExportItemStatus.Pendiente && i.TenantId == C1);
        items.Select(i => i.ProcedureInstanceId).Should().NotIntersectWith(excluidos);
        items.Select(i => i.Position).Should().Equal(Enumerable.Range(0, 348));

        // Orden del listado: la primera página del GET filtrado, sin los excluidos, es el prefijo de los ítems.
        var procedimientos = new ProcedureInstanceRepository(db);
        var (pagina, total) = await new ListProcedureInstancesFilteredHandler(procedimientos)
            .HandleAsync(new ProcedureInstanceListRequest { TenantId = C1, Estados = [TramiteEstado.Preparado], Take = 200 }, Ct);
        total.Should().Be(350);
        var esperado = pagina.Select(p => p.Id).Where(id => !excluidos.Contains(id)).ToList();
        items.Select(i => i.ProcedureInstanceId).Take(esperado.Count).Should().Equal(esperado);

        var audit = await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == loteId, Ct);
        audit.Event.Should().Be(ConsolidadoExportAuditEvent.LoteCreado);
        audit.ActorUserId.Should().Be(Usuario);
        audit.ActorTenantId.Should().Be(C1);
        audit.ActorRoleCode.Should().Be("Radicador");
        audit.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        audit.DocumentType.Should().Be(ConsolidadoExportDocumentType.Consolidado);
        audit.SelectionMode.Should().Be(ConsolidadoExportSelectionMode.Filtro);
        audit.TotalItems.Should().Be(348);
        audit.ExcludedCount.Should().Be(2);
        audit.ReachedTenantIds.Should().Equal(C1);
        audit.ClientIp!.ToString().Should().Be("10.1.2.3");
        audit.FilterSummary.Should().NotContain("QZLT").And.NotContain("6000", "ni placas ni radicados en la auditoría");
        var resumen = JsonDocument.Parse(audit.FilterSummary!).RootElement;
        resumen.GetProperty("modo").GetString().Should().Be("filtro");
        resumen.GetProperty("excluidos").GetProperty("cantidad").GetInt32().Should().Be(2);
        resumen.GetProperty("filtro").GetProperty("estados")[0].GetString().Should().Be(TramiteEstado.Preparado);

        // La DEK queda envuelta y sirve para cifrar/descifrar una parte del lote.
        lote.DekWrapped.Should().NotBeNullOrEmpty();
        lote.DekWrapped!.Length.Should().BeGreaterThan(32, "es la DEK envuelta por Data Protection, no la DEK en claro");
        var cipher = new ConsolidadoLoteCipher(_dataProtection);
        using var claro = new MemoryStream("zip"u8.ToArray());
        using var cifrado = new MemoryStream();
        await cipher.CifrarAsync(lote.DekWrapped, loteId, 1, claro, cifrado, Ct);
        cifrado.Position = 0;
        using var vuelta = new MemoryStream();
        await cipher.DescifrarAsync(lote.DekWrapped, loteId, 1, cifrado, vuelta, Ct);
        vuelta.ToArray().Should().Equal("zip"u8.ToArray());
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_ConLoteEnProceso_DevuelveLoteActivoConSuId_YNoCreaNada()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var activo = await SembrarLoteActivoAsync();

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(new SeleccionPorIds(HierarchyScenario.ProceduresOf(C1).ToList())), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteActivo);
        r.LoteActivoId.Should().Be(activo);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(1);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_CarreraContraElIndiceUnico_El23505SeTraduceALoteActivo_SinPostgresException()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var activo = await SembrarLoteActivoAsync();
        var tramite = HierarchyScenario.DeliveredProcedureOf(C1);

        CrearLoteResultado r;
        await using (var ctx = NewContext())
        {
            // Directo al repositorio: simula que la consulta previa del handler no vio el lote activo.
            r = await new ConsolidadoLoteRepository(ctx).CrearAsync(new NuevoLoteConsolidados
            {
                TenantId = C1,
                UsuarioId = Usuario,
                RolCodigo = "Radicador",
                Origen = ConsolidadoExportOrigin.Tramites,
                TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
                ModoSeleccion = ConsolidadoExportSelectionMode.Ids,
                DekEnvuelta = [1, 2, 3],
                EfectosAceptadosEn = DateTimeOffset.UtcNow,
                Items = [new ProcedureInstanceRef(tramite, C1, "R-1", null)],
                IdsCount = 1,
            }, Ct);
        }

        r.Estado.Should().Be(CrearLoteEstado.LoteActivo);
        r.LoteActivoId.Should().Be(activo);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batch_items")).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_SiFallaLaAuditoria_NoQuedaLoteNiItemsNiAuditoria_NiSePurgaElAnterior()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var anterior = await SembrarLoteTerminalAsync(HierarchyScenario.DeliveredProcedureOf(C1));
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            CREATE OR REPLACE FUNCTION public.tmp_falla_auditoria_13373() RETURNS trigger AS $$
            BEGIN
                IF NEW.event = 'lote_creado' THEN RAISE EXCEPTION 'auditoria no disponible (prueba HU13373)'; END IF;
                RETURN NEW;
            END; $$ LANGUAGE plpgsql;
            CREATE TRIGGER tmp_falla_auditoria_13373 BEFORE INSERT ON tramites.consolidado_export_audit
                FOR EACH ROW EXECUTE FUNCTION public.tmp_falla_auditoria_13373();
            """);
        CrearLoteConsolidadosResultado r;
        try
        {
            await using var ctx = NewContext();
            r = await Handler(ctx).HandleAsync(Gestor(new SeleccionPorIds(HierarchyScenario.ProceduresOf(C1).ToList())), Ct);
        }
        finally
        {
            await ExecAsync(cn,
                """
                DROP TRIGGER IF EXISTS tmp_falla_auditoria_13373 ON tramites.consolidado_export_audit;
                DROP FUNCTION IF EXISTS public.tmp_falla_auditoria_13373();
                """);
        }

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(CrearLoteConsolidadosErrores.LoteNoCreado);
        r.Mensaje.Should().Be(CrearLoteConsolidadosHandler.MensajeNoDisponible);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches WHERE id <> @a", ("a", anterior))).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id <> @a", ("a", anterior))).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0, "ni lote_creado ni lote_purgado");
        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = @a AND dek_wrapped IS NOT NULL AND status = 'completado'",
                ("a", anterior)))
            .Should().Be(1, "la purga del retenido se revierte con el resto");
    }

    // ── AC5 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_LoteTerminalAnterior_SePurgaEnLaMismaTransaccion_ConservandoLaPlaca()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var anterior = await SembrarLoteTerminalAsync(HierarchyScenario.DeliveredProcedureOf(C1));

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(new SeleccionPorIds(HierarchyScenario.ProceduresOf(C1).ToList())), Ct);

        r.Creado.Should().BeTrue(r.Error);
        await using var db = NewContext();
        var viejo = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == anterior, Ct);
        viejo.DekWrapped.Should().BeNull();
        viejo.Status.Should().Be(ConsolidadoExportStatus.Expirado);
        viejo.PurgedAt.Should().NotBeNull();
        (await db.ConsolidadoExportBatchParts.AsNoTracking().Where(p => p.BatchId == anterior).Select(p => p.Status).ToListAsync(Ct))
            .Should().Equal(ConsolidadoExportPartStatus.Purgada);
        (await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == anterior).Select(i => i.Plate).SingleAsync(Ct))
            .Should().Be("PLT001", "S2 = b: la purga no borra la placa");
        var eventos = await db.ConsolidadoExportAuditEntries.AsNoTracking().OrderBy(a => a.Event)
            .Select(a => new { a.Event, a.BatchId, a.OccurredAt }).ToListAsync(Ct);
        eventos.Select(e => (e.Event, e.BatchId)).Should().BeEquivalentTo(
            [(ConsolidadoExportAuditEvent.LoteCreado, r.Lote!.Id), (ConsolidadoExportAuditEvent.LotePurgado, anterior)]);
        eventos.Select(e => e.OccurredAt).Distinct().Should().HaveCount(1, "misma transacción, mismo instante");
        var nuevo = await db.ConsolidadoExportBatches.AsNoTracking().SingleAsync(b => b.Id == r.Lote!.Id, Ct);
        nuevo.Status.Should().Be(ConsolidadoExportStatus.EnCola);
        nuevo.DekWrapped.Should().NotBeNull();
    }

    [PostgresFact]
    public async Task AC5_PurgarAsync_EsReutilizable_SoloSobreTerminalesYUnaVez()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var terminal = await SembrarLoteTerminalAsync(HierarchyScenario.DeliveredProcedureOf(C1));
        var activoUsuarioC2 = Guid.CreateVersion7();
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn,
                """
                INSERT INTO tramites.consolidado_export_batches
                  (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
                   total_items, dek_wrapped, effects_acknowledged_at, created_by)
                VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'en_cola', 0, '\x0a'::bytea, now(), @u);
                """, ("l", activoUsuarioC2), ("c", C2), ("u", HierarchyScenario.UserOf(C2)));
        }

        var ahora = DateTimeOffset.UtcNow;
        await using var ctx = NewContext();
        var repo = new ConsolidadoLoteRepository(ctx);
        (await repo.PurgarAsync(activoUsuarioC2, ahora, Ct)).Should().BeFalse("un lote activo no se purga");
        (await repo.PurgarAsync(terminal, ahora, Ct)).Should().BeTrue();
        (await repo.PurgarAsync(terminal, ahora, Ct)).Should().BeFalse("ya purgado");

        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = @l AND status = 'expirado' AND dek_wrapped IS NULL",
                ("l", terminal))).Should().Be(1);
        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = 'lote_purgado'",
                ("l", terminal))).Should().Be(1);
        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = @l AND dek_wrapped IS NOT NULL",
                ("l", activoUsuarioC2))).Should().Be(1);
    }

    /// <summary>
    /// H1 (security-agent, ADR-0070 adenda v6): ni al crear ni al purgar queda la DEK envuelta en
    /// <c>audit.audit_logs</c>. Si quedara, cualquiera con lectura de la BD (donde también vive el keyring de Data
    /// Protection) la desenvolvería y descifraría las partes de un lote ya purgado.
    /// </summary>
    [PostgresFact]
    public async Task H1_LaDekEnvueltaNuncaLlegaAAuditLogs_NiAlCrearNiAlPurgar()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(new SeleccionPorIds(HierarchyScenario.ProceduresOf(C1).ToList())), Ct);
        r.Creado.Should().BeTrue(r.Error);
        var loteId = r.Lote!.Id;

        // El worker lo termina (UPDATE con la DEK viva) y la retención lo purga con la operación de dominio.
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn,
                """
                UPDATE tramites.consolidado_export_batches
                   SET status = 'completado', finished_at = now() - interval '2 hours', expires_at = now() - interval '1 hour'
                 WHERE id = @l;
                """, ("l", loteId));
        }

        await using (var ctx = NewContext())
            (await new ConsolidadoLoteRepository(ctx).PurgarAsync(loteId, DateTimeOffset.UtcNow, Ct)).Should().BeTrue();

        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = @l AND purged_at IS NOT NULL AND dek_wrapped IS NULL",
                ("l", loteId))).Should().Be(1, "la purga destruyó la DEK de la tabla");
        (await ContarAsync(
                """
                SELECT count(*) FROM audit.audit_logs
                 WHERE schema_name = 'tramites' AND table_name = 'consolidado_export_batches'
                   AND (coalesce(old_data ? 'dek_wrapped', false) OR coalesce(new_data ? 'dek_wrapped', false))
                """)).Should().Be(0, "la DEK envuelta nunca se copia a la auditoría genérica (H1)");
    }

    // ── AC6 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_IdsDeOtraCompaniaEnElCuerpo_NoEntranAlLoteNiCuentan()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();
        var propios = await SembrarTramitesAsync(C1, 3, "QZC1");
        var ajenos = await SembrarTramitesAsync(C2, 2, "QZC2");
        var cuerpo = new List<Guid> { ajenos[0], propios[0], propios[1], ajenos[1], propios[2], HierarchyScenario.DeliveredProcedureOf(C2) };

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(new SeleccionPorIds(cuerpo)), Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(3);
        await using var db = NewContext();
        var items = await db.ConsolidadoExportBatchItems.AsNoTracking().Where(i => i.BatchId == r.Lote.Id).ToListAsync(Ct);
        items.Select(i => i.ProcedureInstanceId).Should().BeEquivalentTo(propios);
        items.Should().OnlyContain(i => i.TenantId == C1);
        (await db.ConsolidadoExportAuditEntries.AsNoTracking().SingleAsync(a => a.BatchId == r.Lote.Id, Ct))
            .ReachedTenantIds.Should().Equal(C1);
    }

    // ── AC7 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC7_LoteDe20000Items_SeCreaEnDiezSegundosOMenos()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        // M1: el tope total por defecto es 10.000; la medición de AC7 lo sube a 20.000 (dentro del CHECK 1–50.000).
        await SembrarSettingsAsync(tope: 20_000);
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn,
                """
                INSERT INTO tramites.procedure_instances
                    (id, tenant_id, procedure_type_id, reference_number, status, plate, vin, created_by_user_id, created_at)
                SELECT gen_random_uuid(), @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                       'P7-' || g, 'preparado', 'RND' || lpad(g::text, 5, '0'), 'VINRND' || g, @u, now() - make_interval(secs => g)
                  FROM generate_series(1, 20000) g;
                """, ("c", C1), ("u", Usuario));
        }

        var reloj = Stopwatch.StartNew();
        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(Preparados()), Ct);
        reloj.Stop();

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(20_000);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l", ("l", r.Lote.Id)))
            .Should().Be(20_000);
        TestContext.Current.TestOutputHelper?.WriteLine($"AC7: lote de 20.000 ítems creado en {reloj.ElapsedMilliseconds} ms");
        reloj.Elapsed.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10), $"tardó {reloj.ElapsedMilliseconds} ms");
    }

    // ── AC8 ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_MotorInactivo_DevuelveMotorInactivo_SinLoteItemsNiAuditoria_YSinPurgarElAnterior()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync(activo: false);
        var anterior = await SembrarLoteTerminalAsync(HierarchyScenario.DeliveredProcedureOf(C1));

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(new SeleccionPorIds(HierarchyScenario.ProceduresOf(C1).ToList())), Ct);

        r.Error.Should().Be(CrearLoteConsolidadosErrores.MotorInactivo);
        r.Mensaje.Should().Be("No se pudo completar la descarga, intente de nuevo");
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(1, "solo el anterior");
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0);
        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = @a AND dek_wrapped IS NOT NULL AND purged_at IS NULL",
                ("a", anterior))).Should().Be(1);
    }

    // ── M1 — tope total configurable ────────────────────────────────────────────────────

    /// <summary>
    /// M1 (épica #13216) — el tope se lee de <c>max_items_per_batch</c> y se compara con la selección resuelta (filtro
    /// menos excluidos). Superarlo no escribe nada: ni lote, ni ítems, ni <c>lote_creado</c>, ni se purga el retenido.
    /// Con las exclusiones que la dejan justo en el tope, el lote se crea.
    /// </summary>
    [PostgresFact]
    public async Task M1_FiltroQueSuperaElTopeDeLaBase_NoEscribeNada_YConExclusionesHastaElTopeSeCrea()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync(tope: 5);
        var anterior = await SembrarLoteTerminalAsync(HierarchyScenario.DeliveredProcedureOf(C1));
        var ids = await SembrarTramitesAsync(C1, 8, "QZM1");

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(Preparados([ids[0]])), Ct);

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        r.Total.Should().Be(7, "8 del filtro menos 1 excluido");
        r.Tope.Should().Be(5);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batches WHERE id <> @a", ("a", anterior))).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id <> @a", ("a", anterior))).Should().Be(0);
        (await ContarAsync("SELECT count(*) FROM tramites.consolidado_export_audit")).Should().Be(0, "ni lote_creado ni lote_purgado");
        (await ContarAsync(
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE id = @a AND dek_wrapped IS NOT NULL AND purged_at IS NULL",
                ("a", anterior))).Should().Be(1, "el retenido no se purga si el lote nuevo no se crea");

        await using (var ctx = NewContext())
            r = await Handler(ctx).HandleAsync(Gestor(Preparados([ids[0], ids[1], ids[2]])), Ct);

        r.Creado.Should().BeTrue(r.Error);
        r.Lote!.TotalItems.Should().Be(5, "exactamente el tope");
    }

    // ── SQL ─────────────────────────────────────────────────────────────────────────────

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default : (T)result;
    }
}
