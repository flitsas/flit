using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13378 (Épica #13216, ADR-0070 D2/D4/D8) — carril de empaquetado contra PostgreSQL real con los CHECK de los DDL
/// 133/134: reclamo de partes <c>FOR UPDATE SKIP LOCKED</c> (lotes <c>en_proceso</c> y <c>empaquetando</c>), cierre
/// condicionado por estado e intentos (AC6), degradación de un incluido a <c>adjunto_no_disponible</c> (AC4), transición
/// terminal con <c>lote_finalizado</c> en la misma transacción (AC2), fallo del lote y cupo liberado (AC3).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// await using var ctx = NewContext();
/// var reclamada = await new ConsolidadoLoteEmpaquetado(ctx).ReclamarSiguienteParteAsync(900, ct);
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteEmpaquetadoIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b3000000-0000-7000-8000-000000013378");
    private const int RetencionHoras = 6;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record ItemSeed(short? Parte, string Estado, string Modo = "existente", long Bytes = 1000, int SegundoProcesado = 0);

    // ── Siembra ─────────────────────────────────────────────────────────────────────────────

    private async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
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

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13378", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        await ExecAsync(
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active, retention_hours)
            VALUES (uuidv7(), true, @r);
            """,
            ("r", RetencionHoras));
    }

    /// <summary>
    /// Lote con sus partes y sus ítems ya terminados (contadores coherentes). <paramref name="vivos"/> ítems quedan
    /// <c>pendiente</c> sin parte. <paramref name="minutosAtras"/> fija <c>created_at</c> (orden de reclamo).
    /// </summary>
    private async Task<(Guid Lote, Guid Usuario, Guid[] Items)> SembrarLoteAsync(
        string sufijo, string estadoLote, (short Numero, string Estado)[] partes, ItemSeed[] items,
        int vivos = 0, int minutosAtras = 0, Guid? usuario = null)
    {
        var lote = Guid.CreateVersion7();
        var u = usuario ?? Guid.CreateVersion7();
        if (usuario is null)
        {
            await ExecAsync(
                "INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES (@u, @e, 'Usuario lote', 'active', now())",
                ("u", u), ("e", $"lote13378-{sufijo}@it.test"));
        }

        var terminal = !ConsolidadoExportStatus.EsActivo(estadoLote);
        await ExecAsync(
            $"""
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, included_count, omitted_count, generated_count, parts_count, dek_wrapped,
               effects_acknowledged_at, created_by, started_at, created_at, finished_at, expires_at)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', @s, @t, @inc, @om, @gen, @pc,
                    {(terminal ? "NULL" : "'\\x010203'::bytea")}, now(), @u, now(), now() - make_interval(mins => @m),
                    {(terminal ? "now(), now() + interval '1 hour'" : "NULL, NULL")});
            """,
            ("l", lote), ("c", Company), ("u", u), ("s", estadoLote), ("t", items.Length + vivos),
            ("inc", items.Count(i => i.Estado == "incluido")), ("om", items.Count(i => i.Estado == "omitido")),
            ("gen", items.Count(i => i.Estado == "incluido" && i.Modo == "generado")), ("pc", (short)partes.Length),
            ("m", minutosAtras));

        foreach (var (numero, estado) in partes)
        {
            await ExecAsync(
                """
                INSERT INTO tramites.consolidado_export_batch_parts (batch_id, part_number, status, pdf_count, omitted_count, lease_until,
                    storage_path, stored_sha256, stored_size_bytes, plain_size_bytes, closed_at)
                VALUES (@l, @n, @s, @pdfs, @om,
                        CASE WHEN @s = 'empaquetando' THEN now() + interval '10 minutes' END,
                        CASE WHEN @s = 'cerrada' THEN 'fm/cerrada' END,
                        CASE WHEN @s = 'cerrada' THEN repeat('b', 64) END,
                        CASE WHEN @s = 'cerrada' THEN 10::bigint END,
                        CASE WHEN @s = 'cerrada' THEN 9::bigint END,
                        CASE WHEN @s = 'cerrada' THEN now() END)
                """,
                ("l", lote), ("n", numero), ("s", estado),
                ("pdfs", items.Count(i => i.Parte == numero && i.Estado == "incluido")),
                ("om", items.Count(i => i.Parte == numero && i.Estado == "omitido")));
        }

        var total = items.Length + vivos;
        var ids = Enumerable.Range(0, total).Select(_ => Guid.CreateVersion7()).ToArray();
        var tramites = Enumerable.Range(0, total).Select(_ => Guid.NewGuid()).ToArray();
        await ExecAsync(
            """
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            SELECT t.id, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), 'IT-13378-' || t.ord,
                   'borrador', 'VIN' || upper(substr(md5(t.id::text), 1, 14)), @u, now()
              FROM unnest(@ids) WITH ORDINALITY AS t(id, ord);
            """,
            ("c", Company), ("u", u), ("ids", tramites));

        for (var k = 0; k < total; k++)
        {
            var seed = k < items.Length ? items[k] : new ItemSeed(null, "pendiente");
            var incluido = seed.Estado == "incluido";
            var omitido = seed.Estado == "omitido";
            await ExecAsync(
                """
                INSERT INTO tramites.consolidado_export_batch_items
                    (id, tenant_id, batch_id, procedure_instance_id, position, status, reference_number, plate, created_by,
                     attachment_id, storage_path, size_bytes, delivery_mode, omission_code, omission_reason, part_number, processed_at)
                VALUES (@id, @c, @l, @tr, @pos, @s, 'R-' || @pos, 'ABC' || @pos, @u,
                        CASE WHEN @inc THEN uuidv7() END, CASE WHEN @inc THEN 'fm/pdf-' || @pos END, CASE WHEN @inc THEN @bytes END,
                        CASE WHEN @inc THEN @modo END, CASE WHEN @om THEN 'acceso_revocado' END, CASE WHEN @om THEN 'Acceso revocado' END,
                        @parte, CASE WHEN @inc OR @om THEN timestamptz '2026-10-07 15:00:00+00' + make_interval(secs => @seg) END)
                """,
                ("id", ids[k]), ("c", Company), ("l", lote), ("tr", tramites[k]), ("pos", k), ("s", seed.Estado), ("u", u),
                ("inc", incluido), ("om", omitido), ("bytes", seed.Bytes), ("modo", seed.Modo),
                ("parte", seed.Parte is { } p ? p : DBNull.Value), ("seg", (double)seed.SegundoProcesado));
        }

        return (lote, u, ids);
    }

    private async Task<(string Estado, short Intentos)> ParteAsync(Guid lote, short numero)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT status, attempts FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND part_number = @n", cn);
        cmd.Parameters.AddWithValue("l", lote);
        cmd.Parameters.AddWithValue("n", numero);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        await r.ReadAsync(Ct);
        return (r.GetString(0), r.GetInt16(1));
    }

    private Task<string> EstadoLoteAsync(Guid lote) => ScalarAsync<string>(
        "SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    private Task<long> AuditoriasAsync(Guid lote, string evento) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = @e", ("l", lote), ("e", evento));

    private async Task<T> ConPuertoAsync<T>(Func<ConsolidadoLoteEmpaquetado, Task<T>> accion)
    {
        await using var ctx = NewContext();
        return await accion(new ConsolidadoLoteEmpaquetado(ctx));
    }

    private static StoredFile Almacenado() => new("fm/parte-cifrada", new string('c', 64), 2048);

    // ── Reclamo ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Reclamo_PartesPendientesDeLotesEnProcesoYEmpaquetando_PorAntiguedadYNumero_NuncaDeUnCancelado()
    {
        await SembrarAsync();
        var (enProceso, _, _) = await SembrarLoteAsync("a", "en_proceso", [(1, "pendiente")], [new(1, "incluido")], vivos: 1, minutosAtras: 30);
        var (empaquetando, _, _) = await SembrarLoteAsync("b", "empaquetando", [(1, "pendiente"), (2, "pendiente")],
            [new(1, "incluido"), new(2, "omitido")], minutosAtras: 20);
        await SembrarLoteAsync("c", "cancelado", [(1, "pendiente")], [new(1, "incluido")], minutosAtras: 40);

        var reclamos = new List<ParteLoteReclamada?>();
        for (var k = 0; k < 4; k++)
            reclamos.Add(await ConPuertoAsync(p => p.ReclamarSiguienteParteAsync(900, Ct)));

        reclamos.Take(3).Select(r => (r!.Lote.Id, r.Parte.PartNumber)).Should().Equal(
            (enProceso, (short)1), (empaquetando, (short)1), (empaquetando, (short)2));
        reclamos[3].Should().BeNull("el lote cancelado no se empaqueta y no queda nada pendiente");
        reclamos[0]!.Parte.Status.Should().Be(ConsolidadoExportPartStatus.Empaquetando);
        reclamos[0]!.Parte.LeaseUntil.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(800));
        reclamos[0]!.Parte.Attempts.Should().Be(0, "reclamar una pendiente no cuenta intento");
        reclamos[0]!.Lote.DekWrapped.Should().NotBeNull("el handler necesita la DEK envuelta");
    }

    [PostgresFact]
    public async Task Reclamo_LeaseVencido_SeRetomaYCuentaLaEjecucionSinCierre_LeaseVigenteNo()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("lease", "empaquetando", [(1, "empaquetando"), (2, "empaquetando")],
            [new(1, "incluido"), new(2, "incluido")]);
        await ExecAsync(
            "UPDATE tramites.consolidado_export_batch_parts SET attempts = 1, lease_until = now() - interval '1 minute' WHERE batch_id = @l AND part_number = 2",
            ("l", lote));

        var r = await ConPuertoAsync(p => p.ReclamarSiguienteParteAsync(900, Ct));

        (r!.Parte.PartNumber, r.Parte.Attempts).Should().Be(((short)2, (short)2));
        (await ConPuertoAsync(p => p.ReclamarSiguienteParteAsync(900, Ct))).Should().BeNull("la parte 1 tiene el lease vigente");
    }

    [PostgresFact]
    public async Task Contenido_PdfPorProcessedAtYPosition_OmitidosPorPosition_SoloDeEsaParte()
    {
        await SembrarAsync();
        var (lote, _, items) = await SembrarLoteAsync("cont", "empaquetando", [(1, "pendiente"), (2, "pendiente")],
        [
            new(1, "incluido", SegundoProcesado: 5),
            new(1, "omitido"),
            new(1, "incluido", SegundoProcesado: 1),
            new(1, "omitido"),
            new(2, "incluido", SegundoProcesado: 0),
        ]);

        var c = await ConPuertoAsync(p => p.LeerContenidoAsync(lote, 1, Ct));

        c.Pdfs.Select(x => x.ItemId).Should().Equal(items[2], items[0]);
        c.Pdfs[0].Should().Match<PdfDeParte>(x => x.Position == 2 && x.ReferenceNumber == "R-2" && x.Plate == "ABC2"
            && x.StoragePath == "fm/pdf-2" && x.TenantId == Company);
        c.Omitidos.Select(x => x.ItemId).Should().Equal(items[1], items[3]);
        c.Omitidos[0].Motivo.Should().Be("Acceso revocado");
    }

    // ── AC1 / AC4 / AC6 — cierre ────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Cierre_ParteCerradaConTamanosShaYRuta()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("cierre", "empaquetando", [(1, "empaquetando")],
            [new(1, "incluido"), new(1, "incluido"), new(1, "incluido"), new(1, "omitido")]);

        var d = await ConPuertoAsync(p => p.CerrarParteAsync(new CierreParteLote(lote, 1, 0, 1500, Almacenado(), []), Ct));

        d.Should().Be(CierreParteDesenlace.Cerrada);
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT status, plain_size_bytes, stored_size_bytes, stored_sha256, storage_path, closed_at IS NOT NULL, lease_until IS NULL,
                   pdf_count, omitted_count
              FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND part_number = 1
            """, cn);
        cmd.Parameters.AddWithValue("l", lote);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        await r.ReadAsync(Ct);
        (r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetString(3), r.GetString(4), r.GetBoolean(5), r.GetBoolean(6), r.GetInt32(7), r.GetInt32(8))
            .Should().Be(("cerrada", 1500L, 2048L, new string('c', 64), "fm/parte-cifrada", true, true, 3, 1));
    }

    [PostgresFact]
    public async Task AC4_Cierre_IncluidoNoDisponible_PasaAOmitidoYAjustaContadoresDeParteYLote()
    {
        await SembrarAsync();
        var (lote, _, items) = await SembrarLoteAsync("ac4", "empaquetando", [(1, "empaquetando")],
            [new(1, "incluido", Modo: "generado"), new(1, "incluido"), new(1, "omitido")]);

        var d = await ConPuertoAsync(p => p.CerrarParteAsync(new CierreParteLote(lote, 1, 0, 900, Almacenado(), [items[0]]), Ct));

        d.Should().Be(CierreParteDesenlace.Cerrada);
        (await ScalarAsync<string>(
            "SELECT status || '|' || omission_code || '|' || omission_reason FROM tramites.consolidado_export_batch_items WHERE id = @i",
            ("i", items[0]))).Should().Be("omitido|adjunto_no_disponible|"
            + Flit.Tramites.Application.UseCases.ProcedureInstances.ConsolidadoErrorTextos.ParaLote(ConsolidadoLoteOmisiones.AdjuntoNoDisponible));
        (await ScalarAsync<string>(
            "SELECT pdf_count || '/' || omitted_count FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l",
            ("l", lote))).Should().Be("1/2");
        (await ScalarAsync<string>(
            "SELECT included_count || '/' || omitted_count || '/' || generated_count FROM tramites.consolidado_export_batches WHERE id = @l",
            ("l", lote))).Should().Be("1/2/0", "el generado deja de contarse como incluido y como generado");
    }

    [PostgresFact]
    public async Task AC6_Cierre_SoloSiLaParteSigueEmpaquetandoConLosMismosIntentos()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("intentos", "empaquetando", [(1, "empaquetando"), (2, "pendiente")],
            [new(1, "incluido"), new(2, "incluido")]);

        (await ConPuertoAsync(p => p.CerrarParteAsync(new CierreParteLote(lote, 1, 3, 10, Almacenado(), []), Ct)))
            .Should().Be(CierreParteDesenlace.NoAplicada, "otra ejecución la retomó (intentos distintos)");
        (await ConPuertoAsync(p => p.CerrarParteAsync(new CierreParteLote(lote, 2, 0, 10, Almacenado(), []), Ct)))
            .Should().Be(CierreParteDesenlace.NoAplicada, "la parte 2 no está empaquetando");

        (await ParteAsync(lote, 1)).Should().Be(("empaquetando", (short)0));
        (await ParteAsync(lote, 2)).Should().Be(("pendiente", (short)0));
    }

    [PostgresFact]
    public async Task AC6_LoteCancelado_LaParteNoSeCierraYQuedaDescartada_SinBinario()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("cancel", "empaquetando", [(1, "empaquetando"), (2, "empaquetando")],
            [new(1, "incluido"), new(2, "incluido")]);
        // Cancelación (#13385) entre el reclamo y el cierre: el lote queda terminal sin DEK.
        await ExecAsync(
            """
            UPDATE tramites.consolidado_export_batches
               SET status = 'cancelado', finished_at = now(), expires_at = now() + interval '1 hour', dek_wrapped = NULL
             WHERE id = @l
            """,
            ("l", lote));

        (await ConPuertoAsync(p => p.DescartarSiLoteInactivoAsync(lote, 1, 0, Ct))).Should().BeTrue("antes de subir: no se sube");
        (await ConPuertoAsync(p => p.CerrarParteAsync(new CierreParteLote(lote, 2, 0, 10, Almacenado(), []), Ct)))
            .Should().Be(CierreParteDesenlace.Descartada, "ya subida: el cierre tampoco procede");

        (await ScalarAsync<long>(
            "SELECT count(*) FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l AND status = 'descartada' AND storage_path IS NULL AND closed_at IS NULL",
            ("l", lote))).Should().Be(2);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Cancelado);
    }

    [PostgresFact]
    public async Task AC6_DescartarSiLoteInactivo_ConLoteVivo_NoEscribe()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("vivo", "en_proceso", [(1, "empaquetando")], [new(1, "incluido")], vivos: 1);

        (await ConPuertoAsync(p => p.DescartarSiLoteInactivoAsync(lote, 1, 0, Ct))).Should().BeFalse();
        (await ParteAsync(lote, 1)).Should().Be(("empaquetando", (short)0));
    }

    // ── AC2 — transición terminal ───────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_TodasCerradas_CompletadoOConOmitidos_ConExpiresYLoteFinalizadoEnLaMismaTransaccion()
    {
        await SembrarAsync();
        var (sinOmitidos, usuario, _) = await SembrarLoteAsync("ok", "empaquetando", [(1, "cerrada")],
            [new(1, "incluido", Modo: "generado"), new(1, "incluido")]);
        var (conOmitidos, _, _) = await SembrarLoteAsync("om", "empaquetando", [(1, "cerrada"), (2, "cerrada")],
            [new(1, "incluido"), new(2, "omitido")]);
        var (noListo, _, _) = await SembrarLoteAsync("nolisto", "empaquetando", [(1, "cerrada"), (2, "pendiente")],
            [new(1, "incluido"), new(2, "incluido")]);

        var candidatos = await ConPuertoAsync(p => p.ObtenerLotesParaFinalizarAsync(10, Ct));
        candidatos.Should().BeEquivalentTo([sinOmitidos, conOmitidos], "el que tiene una parte pendiente no es candidato");

        var a = await ConPuertoAsync(p => p.FinalizarLoteAsync(sinOmitidos, Ct));
        var b = await ConPuertoAsync(p => p.FinalizarLoteAsync(conOmitidos, Ct));
        var c = await ConPuertoAsync(p => p.FinalizarLoteAsync(noListo, Ct));

        a!.Estado.Should().Be(ConsolidadoExportStatus.Completado);
        b!.Estado.Should().Be(ConsolidadoExportStatus.CompletadoConOmitidos);
        c.Should().BeNull();
        a.ExpiresAt.Should().Be(a.FinishedAt.AddHours(RetencionHoras), "expires_at = finished_at + retention_hours");
        (await ScalarAsync<bool>(
            "SELECT finished_at IS NOT NULL AND expires_at = finished_at + make_interval(hours => @h) FROM tramites.consolidado_export_batches WHERE id = @l",
            ("l", sinOmitidos), ("h", RetencionHoras))).Should().BeTrue();

        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT a.total_items, a.included_count, a.omitted_count, a.generated_count, a.actor_user_id, a.actor_role_code,
                       a.occurred_at = b.finished_at
                  FROM tramites.consolidado_export_audit a
                  JOIN tramites.consolidado_export_batches b ON b.id = a.batch_id
                 WHERE a.batch_id = @l AND a.event = 'lote_finalizado'
                """, cn);
            cmd.Parameters.AddWithValue("l", sinOmitidos);
            await using var r = await cmd.ExecuteReaderAsync(Ct);
            (await r.ReadAsync(Ct)).Should().BeTrue("lote_finalizado escrito");
            (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetGuid(4), r.GetString(5), r.GetBoolean(6))
                .Should().Be((2, 2, 0, 1, usuario, "Radicador", true));
        }

        (await ConPuertoAsync(p => p.FinalizarLoteAsync(sinOmitidos, Ct))).Should().BeNull("idempotente: ya es terminal");
        (await AuditoriasAsync(sinOmitidos, "lote_finalizado")).Should().Be(1);
        (await EstadoLoteAsync(noListo)).Should().Be(ConsolidadoExportStatus.Empaquetando);
    }

    [PostgresFact]
    public async Task AC2_Negativo_SiLaAuditoriaFalla_ElLoteNoPasaATerminal()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("audfalla", "empaquetando", [(1, "cerrada")], [new(1, "incluido")]);
        await ExecAsync(
            """
            CREATE OR REPLACE FUNCTION tramites.it13378_rechaza_finalizado() RETURNS trigger AS $$
            BEGIN
                IF NEW.event = 'lote_finalizado' THEN RAISE EXCEPTION 'auditoria caida (simulada)'; END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER it13378_rechaza_finalizado BEFORE INSERT ON tramites.consolidado_export_audit
                FOR EACH ROW EXECUTE FUNCTION tramites.it13378_rechaza_finalizado();
            """);
        try
        {
            var intento = async () => await ConPuertoAsync(p => p.FinalizarLoteAsync(lote, Ct));
            await intento.Should().ThrowAsync<Exception>();
        }
        finally
        {
            await ExecAsync(
                """
                DROP TRIGGER IF EXISTS it13378_rechaza_finalizado ON tramites.consolidado_export_audit;
                DROP FUNCTION IF EXISTS tramites.it13378_rechaza_finalizado();
                """);
        }

        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Empaquetando, "sin auditoría no hay transición");
        (await ScalarAsync<bool>("SELECT finished_at IS NULL FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote)))
            .Should().BeTrue();
    }

    // ── AC3 — fallo ─────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_FalloConIntentosRestantes_LaParteVuelveAPendiente()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("reint", "empaquetando", [(1, "empaquetando")], [new(1, "incluido")]);

        (await ConPuertoAsync(p => p.RegistrarFalloParteAsync(new FalloParteLote(lote, 1, 0, 1, 3), Ct)))
            .Should().Be(FalloParteDesenlace.Reprogramada);

        (await ParteAsync(lote, 1)).Should().Be(("pendiente", (short)1));
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Empaquetando);
        (await ConPuertoAsync(p => p.RegistrarFalloParteAsync(new FalloParteLote(lote, 1, 0, 1, 3), Ct)))
            .Should().Be(FalloParteDesenlace.NoAplicada, "ya no está empaquetando");
    }

    [PostgresFact]
    public async Task AC3_Negativo_ParteAgotaIntentos_LoteFallidoSinPartesDescargables_YCupoLiberado()
    {
        await SembrarAsync();
        var (lote, usuario, items) = await SembrarLoteAsync("agota", "en_proceso", [(1, "cerrada"), (2, "empaquetando"), (3, "pendiente")],
            [new(1, "incluido"), new(2, "incluido"), new(3, "omitido")], vivos: 1);
        await ExecAsync(
            "UPDATE tramites.consolidado_export_batch_parts SET attempts = 2 WHERE batch_id = @l AND part_number = 2", ("l", lote));

        (await ConPuertoAsync(p => p.RegistrarFalloParteAsync(new FalloParteLote(lote, 2, 2, 3, 3), Ct)))
            .Should().Be(FalloParteDesenlace.LoteFallido);

        await AssertLoteFallidoAsync(lote, ConsolidadoLoteErrores.ParteIntentosAgotados);
        (await ParteAsync(lote, 2)).Should().Be(("fallida", (short)3));
        (await ParteAsync(lote, 3)).Estado.Should().Be("descartada");
        (await ParteAsync(lote, 1)).Estado.Should().Be("cerrada", "la purga la pasará a purgada; sin DEK no es legible");
        (await ScalarAsync<string>("SELECT status FROM tramites.consolidado_export_batch_items WHERE id = @i", ("i", items[3])))
            .Should().Be("cancelado", "el ítem vivo no queda colgado en un lote terminal");

        // Cupo liberado: el mismo usuario puede tener otro lote activo (índice único parcial).
        var (nuevo, _, _) = await SembrarLoteAsync("agota-nuevo", "en_cola", [], [], usuario: usuario);
        (await EstadoLoteAsync(nuevo)).Should().Be(ConsolidadoExportStatus.EnCola);
    }

    [PostgresFact]
    public async Task AC3_Negativo_DekQueNoSeDesenvuelve_LoteFallido_YNoSeRepite()
    {
        await SembrarAsync();
        var (lote, _, _) = await SembrarLoteAsync("dek", "empaquetando", [(1, "empaquetando")], [new(1, "incluido")]);

        (await ConPuertoAsync(p => p.FallarLoteAsync(lote, ConsolidadoLoteErrores.DekInvalida, Ct))).Should().BeTrue();
        (await ConPuertoAsync(p => p.FallarLoteAsync(lote, ConsolidadoLoteErrores.DekInvalida, Ct))).Should().BeFalse("ya terminal");

        await AssertLoteFallidoAsync(lote, ConsolidadoLoteErrores.DekInvalida);
        (await ParteAsync(lote, 1)).Estado.Should().Be("descartada");
        (await ConPuertoAsync(p => p.ReclamarSiguienteParteAsync(900, Ct))).Should().BeNull();
    }

    private async Task AssertLoteFallidoAsync(Guid lote, string codigo)
    {
        (await ScalarAsync<string>(
            """
            SELECT status || '|' || error_code || '|' || (dek_wrapped IS NULL)::text || '|'
                   || (expires_at = finished_at + make_interval(hours => @h))::text
              FROM tramites.consolidado_export_batches WHERE id = @l
            """,
            ("l", lote), ("h", RetencionHoras))).Should().Be($"fallido|{codigo}|true|true");
        (await AuditoriasAsync(lote, "lote_finalizado")).Should().Be(1, "lote_finalizado en la misma transacción");
    }

    // ── AC2 — partes en lote_finalizado ─────────────────────────────────────────────────────

    private Task<short> PartesAuditadasAsync(Guid lote) => ScalarAsync<short>(
        """
        SELECT a.parts_count FROM tramites.consolidado_export_audit a
         WHERE a.batch_id = @l AND a.event = 'lote_finalizado'
        """,
        ("l", lote));

    private Task<short> PartesDelLoteAsync(Guid lote) => ScalarAsync<short>(
        "SELECT parts_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    /// <summary>
    /// HU #13378 AC2 — la auditoría <c>lote_finalizado</c> registra «partes» = <c>batches.parts_count</c> (partes en
    /// que se dividió el lote) en la misma transacción, en los tres desenlaces: <c>completado</c>,
    /// <c>completado_con_omitidos</c> y <c>fallido</c> (AC3: cuenta también las partes descartadas o fallidas; que no
    /// sean descargables lo dicen el estado y la DEK destruida; 0 si falló antes de crear ninguna).
    /// </summary>
    [PostgresFact]
    public async Task AC2_LoteFinalizadoRegistraLasPartes_EnCompletadoConOmitidosYFallido()
    {
        await SembrarAsync();
        var (completado, _, _) = await SembrarLoteAsync("p-ok", "empaquetando", [(1, "cerrada")], [new(1, "incluido")]);
        var (conOmitidos, _, _) = await SembrarLoteAsync("p-om", "empaquetando", [(1, "cerrada"), (2, "cerrada")],
            [new(1, "incluido"), new(2, "omitido")]);
        var (agotado, _, _) = await SembrarLoteAsync("p-agota", "empaquetando", [(1, "cerrada"), (2, "empaquetando"), (3, "pendiente")],
            [new(1, "incluido"), new(2, "incluido"), new(3, "omitido")]);
        var (sinPartes, _, _) = await SembrarLoteAsync("p-dek", "en_proceso", [], [], vivos: 1);

        (await ConPuertoAsync(p => p.FinalizarLoteAsync(completado, Ct)))!.Estado.Should().Be(ConsolidadoExportStatus.Completado);
        (await ConPuertoAsync(p => p.FinalizarLoteAsync(conOmitidos, Ct)))!.Estado.Should().Be(ConsolidadoExportStatus.CompletadoConOmitidos);
        (await ConPuertoAsync(p => p.RegistrarFalloParteAsync(new FalloParteLote(agotado, 2, 0, 3, 3), Ct)))
            .Should().Be(FalloParteDesenlace.LoteFallido);
        (await ConPuertoAsync(p => p.FallarLoteAsync(sinPartes, ConsolidadoLoteErrores.DekInvalida, Ct))).Should().BeTrue();

        (await PartesAuditadasAsync(completado)).Should().Be(1);
        (await PartesAuditadasAsync(conOmitidos)).Should().Be(2);
        (await PartesAuditadasAsync(agotado)).Should().Be(3, "fallido: cerrada + fallida + descartada");
        (await PartesAuditadasAsync(sinPartes)).Should().Be(0, "fallido antes de crear partes");
        foreach (var lote in new[] { completado, conOmitidos, agotado, sinPartes })
        {
            (await PartesAuditadasAsync(lote)).Should().Be(await PartesDelLoteAsync(lote), "coincide con batches.parts_count");
            (await AuditoriasAsync(lote, "lote_finalizado")).Should().Be(1);
        }

        (await EstadoLoteAsync(agotado)).Should().Be(ConsolidadoExportStatus.Fallido);
        (await EstadoLoteAsync(sinPartes)).Should().Be(ConsolidadoExportStatus.Fallido);
    }
}
