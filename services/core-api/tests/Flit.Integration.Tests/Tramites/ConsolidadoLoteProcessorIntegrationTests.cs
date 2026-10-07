using System.Collections.Concurrent;
using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13376 (Épica #13216, ADR-0070 D2) — carril de ítems del lote de descarga masiva contra PostgreSQL real:
/// <see cref="ConsolidadoLoteProcessor"/> + el reclamo <c>FOR UPDATE SKIP LOCKED</c> de
/// <see cref="ConsolidadoLoteRepository"/> + el cierre condicionado de <see cref="ConsolidadoLoteItemProceso"/> + el
/// <see cref="ProcesarItemLoteHandler"/> real. El entregador es un doble que aplica la regla del dominio «si existe, se
/// toma; si no, se genera una vez» sobre <c>procedure_instance_attachments</c> (la regla real la cubren #13371/#13375).
/// <para>Uso de ejemplo:
/// <code>
/// await using var carril = Carril(new Entregador(Fixture));
/// await carril.Processor.StartAsync(ct); // reclama, procesa y cierra hasta agotar los ítems
/// </code></para>
/// </summary>
public sealed class ConsolidadoLoteProcessorIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b3000000-0000-7000-8000-000000013376");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(20);

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

    /// <summary>El reset del arnés vacía la tabla de parámetros: cada prueba la siembra.</summary>
    private Task SembrarSettingsAsync(
        bool activo = true, short slots = 2, int timeout = 300, int lease = 600, short maxIntentos = 3) =>
        ExecAsync(
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings
                (id, is_active, item_slots, item_timeout_seconds, item_lease_seconds, max_item_attempts, retry_delay_seconds)
            VALUES (uuidv7(), @a, @s, @t, @l, @m, 5);
            """,
            ("a", activo), ("s", slots), ("t", timeout), ("l", lease), ("m", maxIntentos));

    private async Task SembrarCompaniaAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13376", false, null));
        await ctx.SaveChangesAsync(Ct);
    }

    /// <summary>Lote <c>en_cola</c> de <paramref name="n"/> trámites (un usuario por lote: un lote activo por usuario).</summary>
    private async Task<LoteSembrado> SembrarLoteAsync(int n, string sufijo)
    {
        var lote = Guid.CreateVersion7();
        var usuario = Guid.CreateVersion7();
        var tramites = Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();
        var posiciones = Enumerable.Range(0, n).ToArray();
        await ExecAsync(
            """
            INSERT INTO identity.users (id, email, display_name, status, created_at)
            VALUES (@u, @email, 'Usuario lote', 'active', now());
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, created_by, created_at)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'en_cola', @n, '\x010203'::bytea, now(), @u,
                    clock_timestamp());
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            SELECT t.id, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), 'IT-13376-' || t.pos,
                   'borrador', 'VIN' || upper(substr(md5(t.id::text), 1, 14)), @u, now()
              FROM unnest(@ids, @pos) AS t(id, pos);
            INSERT INTO tramites.consolidado_export_batch_items
                (id, tenant_id, batch_id, procedure_instance_id, position, reference_number, plate, created_by)
            SELECT uuidv7(), @c, @l, t.id, t.pos, 'IT-13376-' || t.pos, NULL, @u
              FROM unnest(@ids, @pos) AS t(id, pos);
            """,
            ("l", lote), ("u", usuario), ("email", $"lote13376-{sufijo}@it.test"), ("c", Company), ("n", n),
            ("ids", tramites), ("pos", posiciones));
        return new LoteSembrado(lote, tramites);
    }

    private async Task<(Guid Id, string Sha, DateTime Subido)> SembrarConsolidadoAsync(Guid tramite)
    {
        var id = Guid.CreateVersion7();
        var sha = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray());
        await ExecAsync(
            """
            INSERT INTO tramites.procedure_instance_attachments
                (id, tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path, uploaded_at)
            VALUES (@id, @c, @t, 'consolidado', 'consolidado.pdf', 'application/pdf', 100, @sha, 'fm/previo', now() - interval '1 day');
            """,
            ("id", id), ("c", Company), ("t", tramite), ("sha", sha));
        var subido = await ScalarAsync<DateTime>(
            "SELECT uploaded_at FROM tramites.procedure_instance_attachments WHERE id = @id", ("id", id));
        return (id, sha, subido);
    }

    private Task<long> ContarAsync(Guid lote, string estado) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND status = @s",
        ("l", lote), ("s", estado));

    private Task<long> CerradosAsync(Guid lote) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND status IN ('incluido', 'omitido')",
        ("l", lote));

    private Task<string> EstadoLoteAsync(Guid lote) => ScalarAsync<string>(
        "SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    private Task<long> ConsolidadosAsync(Guid tramite) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @t AND tipo = 'consolidado'",
        ("t", tramite));

    private static async Task EsperarAsync(Func<Task<bool>> condicion, string porque)
    {
        var limite = DateTime.UtcNow + Espera;
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
                return;
            await Task.Delay(50, Ct);
        }

        (await condicion()).Should().BeTrue(porque);
    }

    // ── Carril ──────────────────────────────────────────────────────────────────────────────

    private Carril NuevoCarril(Entregador entregador)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Fixture.CreateDbContext());
        services.AddScoped<IConsolidadoLoteRepository>(sp => new ConsolidadoLoteRepository(sp.GetRequiredService<FlitDbContext>()));
        services.AddScoped<IConsolidadoLoteItemProceso>(sp => new ConsolidadoLoteItemProceso(sp.GetRequiredService<FlitDbContext>()));
        services.AddSingleton<IConsolidadoLoteAccessChecker>(new AccesoSiempre());
        services.AddScoped<ILoteItemEntregador>(sp => entregador.Para(sp.GetRequiredService<FlitDbContext>()));
        services.AddScoped<ILoteItemOrigen, TramitesLoteItemOrigen>();
        services.AddScoped<LoteItemOrigenPorOrigen>();
        services.AddScoped(sp => new ProcesarItemLoteHandler(
            sp.GetRequiredService<LoteItemOrigenPorOrigen>(),
            sp.GetRequiredService<IConsolidadoLoteItemProceso>(),
            sp.GetRequiredService<ILogger<ProcesarItemLoteHandler>>()));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var processor = new ConsolidadoLoteProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ConsolidadoLoteProcessor>.Instance,
            new ConsolidadoLoteProcessorOptions(TimeSpan.FromMilliseconds(100), TimeSpan.Zero),
            TimeProvider.System);
        return new Carril(provider, processor);
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_ConItemSlots2_NuncaHayMasDeDosProcesando_Y_CierraTodo()
    {
        await SembrarSettingsAsync(slots: 2);
        await SembrarCompaniaAsync();
        var lotes = new[] { await SembrarLoteAsync(4, "a"), await SembrarLoteAsync(4, "b"), await SembrarLoteAsync(4, "c") };
        var entregador = new Entregador(Fixture) { Retardo = TimeSpan.FromMilliseconds(150), MuestrearProcesando = true };

        await using (var carril = NuevoCarril(entregador))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => (await Task.WhenAll(lotes.Select(l => CerradosAsync(l.Id)))).Sum() == 12,
                "los 12 ítems de los 3 lotes se cierran");

            // HU #13377: al terminar su carril de ítems, el procesador cierra cada lote con su única parte (4 PDF < N).
            foreach (var l in lotes)
                await EsperarAsync(async () => await EstadoLoteAsync(l.Id) == ConsolidadoExportStatus.Empaquetando,
                    "el ciclo siguiente cierra el carril del lote");
            await carril.Processor.StopAsync(Ct);
        }

        entregador.MaxConcurrencia.Should().Be(2, "item_slots = 2 se alcanza y no se supera en la instancia");
        entregador.MaxProcesandoEnBd.Should().BeLessThanOrEqualTo(2);
        foreach (var l in lotes)
        {
            (await ContarAsync(l.Id, ConsolidadoExportItemStatus.Incluido)).Should().Be(4);
        }

        foreach (var l in lotes)
        {
            (await ScalarAsync<short>("SELECT parts_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", l.Id)))
                .Should().Be(1);
            (await ScalarAsync<long>(
                "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND part_number = 1", ("l", l.Id)))
                .Should().Be(4);
        }

        await using var ctx = NewContext();
        (await new ConsolidadoLoteRepository(ctx).ObtenerLotesConCarrilTerminadoAsync(10, Ct))
            .Should().BeEmpty("ya no quedan lotes en_proceso con el carril terminado");
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_Reclamo_AlternaEntreLotes_PorLastClaimedAt_Y_EnColaHastaElPrimerReclamo()
    {
        await SembrarSettingsAsync();
        await SembrarCompaniaAsync();
        var a = await SembrarLoteAsync(3, "a");
        var b = await SembrarLoteAsync(3, "b");
        await using var ctx = NewContext();
        var repo = new ConsolidadoLoteRepository(ctx);

        var orden = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            if (i == 1)
                (await EstadoLoteAsync(b.Id)).Should().Be(ConsolidadoExportStatus.EnCola, "B aún no tiene ningún ítem reclamado");
            var r = await repo.ReclamarSiguienteItemAsync("it-13376", 600, Ct);
            r.Should().NotBeNull();
            r!.Item.Status.Should().Be(ConsolidadoExportItemStatus.Procesando);
            r.Item.LeaseUntil.Should().NotBeNull();
            r.Item.Attempts.Should().Be(0, "el reclamo de un pendiente no suma intentos (contrato de #13375)");
            r.Lote.Status.Should().Be(ConsolidadoExportStatus.EnProceso);
            r.Lote.Id.Should().Be(r.Item.BatchId);
            orden.Add(r.Item.BatchId);
        }

        orden.Should().Equal([a.Id, b.Id, a.Id, b.Id, a.Id, b.Id], "un ítem por lote y turno por last_claimed_at");
        (await repo.ReclamarSiguienteItemAsync("it-13376", 600, Ct)).Should().BeNull("todo está en procesando con lease vigente");
        (await ScalarAsync<bool>("SELECT started_at IS NOT NULL FROM tramites.consolidado_export_batches WHERE id = @l", ("l", b.Id)))
            .Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC2_LoteB_Pequeno_TerminaAntesQueElLoteA_Grande()
    {
        await SembrarSettingsAsync(slots: 1);
        await SembrarCompaniaAsync();
        var a = await SembrarLoteAsync(40, "a");
        var entregador = new Entregador(Fixture) { Retardo = TimeSpan.FromMilliseconds(40) };

        LoteSembrado b;
        await using (var carril = NuevoCarril(entregador))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => await CerradosAsync(a.Id) >= 3, "A ya está en proceso");
            b = await SembrarLoteAsync(5, "b");
            await EsperarAsync(async () => await CerradosAsync(a.Id) + await CerradosAsync(b.Id) == 45, "A y B terminan");
            await carril.Processor.StopAsync(Ct);
        }

        var finA = await ScalarAsync<DateTime>(
            "SELECT max(processed_at) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l", ("l", a.Id));
        var finB = await ScalarAsync<DateTime>(
            "SELECT max(processed_at) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l", ("l", b.Id));
        finB.Should().BeBefore(finA, "B (5) termina su carril antes que A (40)");
        var deAMientrasB = await ScalarAsync<long>(
            """
            SELECT count(*) FROM tramites.consolidado_export_batch_items
             WHERE batch_id = @a AND processed_at > (SELECT min(created_at) FROM tramites.consolidado_export_batch_items WHERE batch_id = @b)
               AND processed_at <= @finB
            """,
            ("a", a.Id), ("b", b.Id), ("finB", finB));
        deAMientrasB.Should().BeLessThanOrEqualTo(6, "con un slot los turnos se alternan: ≈ un ítem de A por cada ítem de B");
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_CaidaAl40PorCiento_ReanudaSinDobleGeneracion()
    {
        await SembrarSettingsAsync(slots: 1);
        await SembrarCompaniaAsync();
        var lote = await SembrarLoteAsync(10, "mixto");
        var previos = new Dictionary<Guid, (Guid Id, string Sha, DateTime Subido)>();
        foreach (var t in lote.Tramites.Take(4))
            previos[t] = await SembrarConsolidadoAsync(t);

        // 1ª instancia: el 5.º trámite (sin consolidado) persiste su consolidado y el proceso «muere» antes de cerrar.
        var caida = new Entregador(Fixture) { Retardo = TimeSpan.FromMilliseconds(20), CaerTrasPersistir = lote.Tramites[4] };
        await using (var carril = NuevoCarril(caida))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(() => Task.FromResult(caida.Caido), "el 5.º ítem persistió y quedó colgado");
            await carril.Processor.StopAsync(Ct);
        }

        (await CerradosAsync(lote.Id)).Should().Be(4, "40 % procesado");
        (await ContarAsync(lote.Id, ConsolidadoExportItemStatus.Procesando)).Should().Be(1, "el ítem en vuelo no se cerró");
        (await ConsolidadosAsync(lote.Tramites[4])).Should().Be(1);

        // El lease vence (el proceso no volvió a tiempo): el ítem vuelve a ser reclamable.
        await ExecAsync(
            "UPDATE tramites.consolidado_export_batch_items SET lease_until = now() - interval '1 second' WHERE batch_id = @l AND status = 'procesando'",
            ("l", lote.Id));

        var reinicio = new Entregador(Fixture);
        await using (var carril = NuevoCarril(reinicio))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => await CerradosAsync(lote.Id) == 10, "el lote continúa desde los no procesados");
            await carril.Processor.StopAsync(Ct);
        }

        reinicio.Llamadas.Keys.Should().BeEquivalentTo(lote.Tramites.Skip(4), "los 4 cerrados no se reprocesan");
        foreach (var t in lote.Tramites)
            (await ConsolidadosAsync(t)).Should().Be(1, "exactamente una fila consolidado por trámite");
        foreach (var (t, previo) in previos)
        {
            (await ScalarAsync<long>(
                    "SELECT count(*) FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @t AND tipo = 'consolidado' AND id = @id AND sha256 = @sha AND uploaded_at = @sub",
                    ("t", t), ("id", previo.Id), ("sha", previo.Sha), ("sub", previo.Subido)))
                .Should().Be(1, "el consolidado previo conserva id, sha256 y uploaded_at");
        }

        var item5 = await ScalarAsync<string>(
            "SELECT delivery_mode || '|' || attempts FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND position = 4",
            ("l", lote.Id));
        item5.Should().Be("existente|1", "el reintento toma el consolidado que dejó la ejecución caída y cuenta la caída");
        (await ScalarAsync<int>("SELECT included_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote.Id))).Should().Be(10);
        (await ScalarAsync<int>("SELECT generated_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote.Id))).Should().Be(5);
    }

    // ── AC4 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_ElTimeoutCancelaLaEjecucion_AntesDeQueVenzaElLease()
    {
        await SembrarSettingsAsync(slots: 1, timeout: 1, lease: 3);
        await SembrarCompaniaAsync();
        var lote = await SembrarLoteAsync(1, "timeout");
        var entregador = new Entregador(Fixture) { Bloquear = true };

        await using (var carril = NuevoCarril(entregador))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(async () => await ContarAsync(lote.Id, ConsolidadoExportItemStatus.Pendiente) == 1
                                           && entregador.Cancelaciones.Count == 1, "el ítem excedió el timeout");
            await carril.Processor.StopAsync(Ct);
        }

        entregador.Cancelaciones.Should().ContainSingle()
            .Which.Should().BeTrue("la ejecución se canceló con el lease aún vigente (todavía no reclamable)");
        entregador.Llamadas.Values.Sum().Should().Be(1, "nadie lo re-reclamó mientras la primera ejecución vivía");
        (await ScalarAsync<short>("SELECT attempts FROM tramites.consolidado_export_batch_items WHERE batch_id = @l", ("l", lote.Id)))
            .Should().Be(1, "el timeout cuenta como fallo técnico");
    }

    // ── AC6 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_CierreConLeaseVencido_NoSobrescribeElItem()
    {
        await SembrarSettingsAsync();
        await SembrarCompaniaAsync();
        var lote = await SembrarLoteAsync(1, "vencido");
        var item = await ScalarAsync<Guid>("SELECT id FROM tramites.consolidado_export_batch_items WHERE batch_id = @l", ("l", lote.Id));
        await ExecAsync(
            "UPDATE tramites.consolidado_export_batch_items SET status = 'procesando', lease_until = now() - interval '1 second' WHERE id = @i",
            ("i", item));
        await using var ctx = NewContext();
        var proceso = new ConsolidadoLoteItemProceso(ctx);
        var ahora = DateTimeOffset.UtcNow;

        (await proceso.MarcarIncluidoAsync(new LoteItemIncluido(lote.Id, item,
            new LoteItemAdjunto(Guid.NewGuid(), "fm/x", 1, "sha", "c.pdf"), ConsolidadoExportDeliveryMode.Generado, ahora), Ct))
            .Should().BeFalse("la reserva venció");
        (await proceso.MarcarOmitidoAsync(new LoteItemOmitido(lote.Id, item, ConsolidadoLoteOmisiones.SinAdjuntos, "x", 0, ahora), Ct))
            .Should().BeFalse();
        (await proceso.ReprogramarAsync(new LoteItemReintento(lote.Id, item, 1, ahora), Ct)).Should().BeFalse();

        (await ContarAsync(lote.Id, ConsolidadoExportItemStatus.Procesando)).Should().Be(1, "el ítem queda reclamable, intacto");
        (await ScalarAsync<int>("SELECT included_count + omitted_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote.Id)))
            .Should().Be(0);
    }

    [PostgresFact]
    public async Task AC6_LoteCanceladoDuranteLaEjecucion_ElCierreNoSobrescribe()
    {
        await SembrarSettingsAsync(slots: 1);
        await SembrarCompaniaAsync();
        var lote = await SembrarLoteAsync(1, "cancelado");
        var entregador = new Entregador(Fixture) { CancelarItemDuranteEjecucion = true };

        await using (var carril = NuevoCarril(entregador))
        {
            await carril.Processor.StartAsync(Ct);
            await EsperarAsync(() => Task.FromResult(entregador.Llamadas.Values.Sum() == 1 && entregador.Terminadas == 1),
                "la ejecución terminó");
            await Task.Delay(300, Ct);
            await carril.Processor.StopAsync(Ct);
        }

        (await ContarAsync(lote.Id, ConsolidadoExportItemStatus.Cancelado)).Should().Be(1, "el cierre no sobrescribe un ítem cancelado");
        (await ScalarAsync<int>("SELECT included_count + generated_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote.Id)))
            .Should().Be(0);
    }

    // ── AC6 bis (S4) ────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6bis_MotorApagado_NoReclama_Y_AlEncenderRetomaSinDobleGeneracion()
    {
        await SembrarSettingsAsync(activo: false);
        await SembrarCompaniaAsync();
        var lote = await SembrarLoteAsync(3, "apagado");
        var entregador = new Entregador(Fixture);

        await using (var carril = NuevoCarril(entregador))
        {
            await carril.Processor.StartAsync(Ct);
            await Task.Delay(800, Ct);
            (await ContarAsync(lote.Id, ConsolidadoExportItemStatus.Pendiente)).Should().Be(3, "con el motor apagado no se reclama nada");
            (await EstadoLoteAsync(lote.Id)).Should().Be(ConsolidadoExportStatus.EnCola, "el lote conserva su estado");
            (await ScalarAsync<bool>("SELECT last_claimed_at IS NULL FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote.Id)))
                .Should().BeTrue();
            entregador.Llamadas.Should().BeEmpty();

            await ExecAsync("UPDATE tramites.consolidado_export_settings SET is_active = true");
            await EsperarAsync(async () => await CerradosAsync(lote.Id) == 3, "al encender, retoma los pendientes");
            await carril.Processor.StopAsync(Ct);
        }

        entregador.Llamadas.Values.Should().AllSatisfy(v => v.Should().Be(1));
        foreach (var t in lote.Tramites)
            (await ConsolidadosAsync(t)).Should().Be(1);
    }

    // ── Lote sin ítems (hallazgo de #13373) ─────────────────────────────────────────────────

    [PostgresFact]
    public async Task LoteSinItems_PasaAEnProceso_Y_QuedaListoParaElCierreDelLote()
    {
        await SembrarSettingsAsync();
        await SembrarCompaniaAsync();
        var vacio = await SembrarLoteAsync(0, "vacio");
        var conItems = await SembrarLoteAsync(1, "lleno");

        await using var ctx = NewContext();
        var repo = new ConsolidadoLoteRepository(ctx);
        (await repo.ObtenerLotesConCarrilTerminadoAsync(10, Ct)).Should().BeEmpty("en_cola no es carril terminado");

        (await repo.IniciarLotesSinItemsAsync(Ct)).Should().Be(1);
        (await repo.IniciarLotesSinItemsAsync(Ct)).Should().Be(0, "idempotente");
        (await EstadoLoteAsync(vacio.Id)).Should().Be(ConsolidadoExportStatus.EnProceso);
        (await EstadoLoteAsync(conItems.Id)).Should().Be(ConsolidadoExportStatus.EnCola);
        (await ScalarAsync<bool>("SELECT started_at IS NOT NULL FROM tramites.consolidado_export_batches WHERE id = @l", ("l", vacio.Id)))
            .Should().BeTrue();
        (await repo.ObtenerLotesConCarrilTerminadoAsync(10, Ct)).Should().Equal([vacio.Id]);
    }

    // ── Dobles ──────────────────────────────────────────────────────────────────────────────

    private sealed record LoteSembrado(Guid Id, Guid[] Tramites);

    private sealed class Carril(ServiceProvider provider, ConsolidadoLoteProcessor processor) : IAsyncDisposable
    {
        public ConsolidadoLoteProcessor Processor { get; } = processor;

        public async ValueTask DisposeAsync()
        {
            Processor.Dispose();
            await provider.DisposeAsync();
        }
    }

    private sealed class AccesoSiempre : IConsolidadoLoteAccessChecker
    {
        public Task<bool> TieneAccesoAsync(LoteItemContexto contexto, CancellationToken ct = default) => Task.FromResult(true);
    }

    /// <summary>
    /// Entregador de prueba con la regla del dominio: si el trámite ya tiene consolidado se toma tal cual
    /// (<c>existente</c>); si no, se inserta uno (<c>generado</c>). Registra llamadas, concurrencia y cancelaciones.
    /// </summary>
    private sealed class Entregador(PostgresDatabaseFixture fixture)
    {
        private readonly PostgresDatabaseFixture _fixture = fixture;
        private int _enCurso;
        private int _terminadas;

        public TimeSpan Retardo { get; init; }
        public bool MuestrearProcesando { get; init; }
        public bool Bloquear { get; init; }
        public bool CancelarItemDuranteEjecucion { get; init; }
        public Guid? CaerTrasPersistir { get; init; }

        public ConcurrentDictionary<Guid, int> Llamadas { get; } = new();
        public ConcurrentQueue<bool> Cancelaciones { get; } = new();
        public int MaxConcurrencia { get; private set; }
        public long MaxProcesandoEnBd { get; private set; }
        public bool Caido { get; private set; }
        public int Terminadas => _terminadas;

        public ILoteItemEntregador Para(FlitDbContext db) => new Instancia(this, db);

        private sealed class Instancia(Entregador e, FlitDbContext db) : ILoteItemEntregador
        {
            public async Task<LoteItemEntregaResult> EntregarAsync(LoteItemEntregaRequest request, CancellationToken ct = default)
            {
                e.Llamadas.AddOrUpdate(request.ProcedureInstanceId, 1, (_, n) => n + 1);
                var actual = Interlocked.Increment(ref e._enCurso);
                lock (e)
                    e.MaxConcurrencia = Math.Max(e.MaxConcurrencia, actual);
                try
                {
                    if (e.MuestrearProcesando)
                    {
                        await using var cn = await e._fixture.OpenConnectionAsync();
                        await using var cmd = new NpgsqlCommand(
                            "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE status = 'procesando'", cn);
                        var enBd = (long)(await cmd.ExecuteScalarAsync(CancellationToken.None))!;
                        lock (e)
                            e.MaxProcesandoEnBd = Math.Max(e.MaxProcesandoEnBd, enBd);
                    }

                    if (e.Bloquear)
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            await using var cn = await e._fixture.OpenConnectionAsync();
                            await using var cmd = new NpgsqlCommand(
                                "SELECT clock_timestamp() < lease_until FROM tramites.consolidado_export_batch_items WHERE procedure_instance_id = @t",
                                cn);
                            cmd.Parameters.AddWithValue("t", request.ProcedureInstanceId);
                            e.Cancelaciones.Enqueue((bool)(await cmd.ExecuteScalarAsync(CancellationToken.None))!);
                            throw;
                        }
                    }

                    if (e.Retardo > TimeSpan.Zero)
                        await Task.Delay(e.Retardo, ct);

                    var existente = await db.ProcedureInstanceAttachments.AsNoTracking()
                        .Where(a => a.ProcedureInstanceId == request.ProcedureInstanceId && a.Tipo == "consolidado")
                        .FirstOrDefaultAsync(ct);
                    if (existente is not null)
                        return LoteItemEntregaResult.Incluido(Adjunto(existente), LoteDeliveryMode.Existente);

                    var nuevo = new ProcedureInstanceAttachment
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = request.TenantId,
                        ProcedureInstanceId = request.ProcedureInstanceId,
                        Tipo = "consolidado",
                        Filename = "consolidado.pdf",
                        Mimetype = "application/pdf",
                        SizeBytes = 200,
                        Sha256 = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()) + Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()),
                        StoragePath = $"fm/{request.ProcedureInstanceId:N}",
                        UploadedAt = DateTimeOffset.UtcNow,
                    };
                    db.ProcedureInstanceAttachments.Add(nuevo);
                    await db.SaveChangesAsync(ct);

                    if (e.CaerTrasPersistir == request.ProcedureInstanceId && !e.Caido)
                    {
                        e.Caido = true;
                        await Task.Delay(Timeout.Infinite, ct); // «muere» antes de cerrar el ítem
                    }

                    if (e.CancelarItemDuranteEjecucion)
                    {
                        await using var cn = await e._fixture.OpenConnectionAsync();
                        await using var cmd = new NpgsqlCommand(
                            "UPDATE tramites.consolidado_export_batch_items SET status = 'cancelado', lease_until = NULL WHERE procedure_instance_id = @t",
                            cn);
                        cmd.Parameters.AddWithValue("t", request.ProcedureInstanceId);
                        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
                    }

                    return LoteItemEntregaResult.Incluido(Adjunto(nuevo), LoteDeliveryMode.Generado);
                }
                finally
                {
                    Interlocked.Decrement(ref e._enCurso);
                    Interlocked.Increment(ref e._terminadas);
                }
            }

            private static LoteItemAdjunto Adjunto(ProcedureInstanceAttachment a) =>
                new(a.Id, a.StoragePath, a.SizeBytes, a.Sha256, a.Filename);
        }
    }
}
