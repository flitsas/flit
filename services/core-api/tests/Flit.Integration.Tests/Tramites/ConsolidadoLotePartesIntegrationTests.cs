using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13377 (Épica #13216, diseño §3) — asignación de partes contra PostgreSQL real con los CHECK y la FK compuesta
/// <c>(batch_id, part_number)</c> del DDL 134:
/// <list type="bullet">
///   <item>parcial, en la transacción del cierre de un ítem incluido (<see cref="ConsolidadoLoteItemProceso"/>, lock del
///   lote): solo partes llenas por N o M;</item>
///   <item>final, en <see cref="ConsolidadoLoteRepository.CerrarCarrilAsync"/>: el resto, la parte solo con
///   <c>omitidos.csv</c> y la transición <c>en_proceso → empaquetando</c>.</item>
/// </list>
/// <para>Uso de ejemplo:
/// <code>
/// await proceso.MarcarIncluidoAsync(new(lote, item, adjunto, "existente", ahora), ct); // puede crear la parte k
/// var r = await new ConsolidadoLoteRepository(ctx).CerrarCarrilAsync(lote, ct);        // última parte + empaquetando
/// </code></para>
/// </summary>
public sealed class ConsolidadoLotePartesIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Company = new("b3000000-0000-7000-8000-000000013377");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 7, 15, 0, 0, TimeSpan.Zero);
    private const long MiB = 1024L * 1024L;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    private async Task<List<(short Parte, string Estado, int Pdfs, int Omitidos, long Bytes)>> PartesAsync(Guid lote)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT p.part_number, p.status, p.pdf_count, p.omitted_count,
                   COALESCE((SELECT sum(i.size_bytes) FROM tramites.consolidado_export_batch_items i
                              WHERE i.batch_id = p.batch_id AND i.part_number = p.part_number AND i.status = 'incluido'), 0)::bigint
              FROM tramites.consolidado_export_batch_parts p
             WHERE p.batch_id = @l
             ORDER BY p.part_number
            """,
            cn);
        cmd.Parameters.AddWithValue("l", lote);
        await using var r = await cmd.ExecuteReaderAsync(Ct);
        var partes = new List<(short, string, int, int, long)>();
        while (await r.ReadAsync(Ct))
            partes.Add((r.GetInt16(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt64(4)));
        return partes;
    }

    private Task<string> EstadoLoteAsync(Guid lote) => ScalarAsync<string>(
        "SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    private Task<short> PartsCountAsync(Guid lote) => ScalarAsync<short>(
        "SELECT parts_count FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    private Task<long> ItemsEnParteAsync(Guid lote, short parte) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND part_number = @p",
        ("l", lote), ("p", parte));

    private async Task SembrarAsync(int maxPdfs = 500, int maxMb = 250)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-13377", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        await ExecAsync(
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active, max_pdfs_per_part, max_mb_per_part)
            VALUES (uuidv7(), true, @n, @m);
            """,
            ("n", maxPdfs), ("m", maxMb));
    }

    /// <summary>Lote <c>en_proceso</c> con <paramref name="n"/> ítems <c>procesando</c> (lease vigente), como tras el reclamo.</summary>
    private async Task<(Guid Lote, Guid[] Items)> SembrarLoteAsync(int n, string sufijo, string estadoLote = "en_proceso")
    {
        var lote = Guid.CreateVersion7();
        var usuario = Guid.CreateVersion7();
        var tramites = Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToArray();
        var items = Enumerable.Range(0, n).Select(_ => Guid.CreateVersion7()).ToArray();
        var posiciones = Enumerable.Range(0, n).ToArray();
        await ExecAsync(
            """
            INSERT INTO identity.users (id, email, display_name, status, created_at)
            VALUES (@u, @email, 'Usuario lote', 'active', now());
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, dek_wrapped, effects_acknowledged_at, created_by, started_at)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', 'en_proceso', @n, '\x010203'::bytea, now(), @u, now());
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            SELECT t.id, @c, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), 'IT-13377-' || t.pos,
                   'borrador', 'VIN' || upper(substr(md5(t.id::text), 1, 14)), @u, now()
              FROM unnest(@ids, @pos) AS t(id, pos);
            INSERT INTO tramites.consolidado_export_batch_items
                (id, tenant_id, batch_id, procedure_instance_id, position, status, lease_until, claimed_by,
                 reference_number, plate, created_by)
            SELECT t.item, @c, @l, t.id, t.pos, 'procesando', now() + interval '10 minutes', 'slot-1',
                   'IT-13377-' || t.pos, 'ABC' || t.pos, @u
              FROM unnest(@items, @ids, @pos) AS t(item, id, pos);
            """,
            ("l", lote), ("u", usuario), ("email", $"lote13377-{sufijo}@it.test"), ("c", Company), ("n", n),
            ("ids", tramites), ("items", items), ("pos", posiciones));
        if (estadoLote == ConsolidadoExportStatus.Cancelado)
        {
            await ExecAsync(
                """
                UPDATE tramites.consolidado_export_batches
                   SET status = 'cancelado', finished_at = now(), expires_at = now() + interval '1 hour', dek_wrapped = NULL
                 WHERE id = @l
                """,
                ("l", lote));
        }

        return (lote, items);
    }

    private static LoteItemAdjunto Adjunto(long bytes) =>
        new(Guid.CreateVersion7(), "fm/consolidado-13377", bytes, "sha", "consolidado.pdf");

    private async Task IncluirAsync(Guid lote, Guid item, long bytes, int segundo)
    {
        await using var ctx = NewContext();
        (await new ConsolidadoLoteItemProceso(ctx).MarcarIncluidoAsync(
            new LoteItemIncluido(lote, item, Adjunto(bytes), ConsolidadoExportDeliveryMode.Existente, Ahora.AddSeconds(segundo)), Ct))
            .Should().BeTrue();
    }

    private async Task OmitirAsync(Guid lote, Guid item, int segundo)
    {
        await using var ctx = NewContext();
        (await new ConsolidadoLoteItemProceso(ctx).MarcarOmitidoAsync(
            new LoteItemOmitido(lote, item, ConsolidadoLoteOmisiones.AccesoRevocado, "Acceso revocado", 0, Ahora.AddSeconds(segundo)), Ct))
            .Should().BeTrue();
    }

    private async Task<CierreCarrilResultado> CerrarCarrilAsync(Guid lote)
    {
        await using var ctx = NewContext();
        return await new ConsolidadoLoteRepository(ctx).CerrarCarrilAsync(lote, Ct);
    }

    // ── AC1 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_NMasUno_LaParteLlenaSaleAlCerrarElItem_YElCierreDelCarrilDaExactamenteDos()
    {
        await SembrarAsync(maxPdfs: 3);
        var (lote, items) = await SembrarLoteAsync(4, "ac1");

        for (var k = 0; k < 3; k++)
            await IncluirAsync(lote, items[k], MiB, k);

        (await PartesAsync(lote)).Should().Equal([(1, "pendiente", 3, 0, 3 * MiB)],
            "al cerrar el tercer PDF (N = 3) la parte 1 queda lista para empaquetar sin esperar al lote");
        (await PartsCountAsync(lote)).Should().Be(1);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.EnProceso);

        await IncluirAsync(lote, items[3], MiB, 3);
        (await PartesAsync(lote)).Should().HaveCount(1, "el cuarto PDF espera: su parte no está llena");

        var r = await CerrarCarrilAsync(lote);

        r.Should().Be(new CierreCarrilResultado(true, 1, 2));
        var partes = await PartesAsync(lote);
        partes.Should().Equal([(1, "pendiente", 3, 0, 3 * MiB), (2, "pendiente", 1, 0, MiB)]);
        partes.Should().OnlyContain(p => p.Pdfs <= 3);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Empaquetando);
        (await PartsCountAsync(lote)).Should().Be(2);
        (await ScalarAsync<long>(
            "SELECT count(*) FROM tramites.consolidado_export_batch_items WHERE batch_id = @l AND part_number IS NULL", ("l", lote)))
            .Should().Be(0, "ningún ítem queda sin parte");

        (await CerrarCarrilAsync(lote)).Should().Be(CierreCarrilResultado.NoAplicado, "idempotente: ya está empaquetando");
        (await PartesAsync(lote)).Should().HaveCount(2);
    }

    // ── AC2 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_PdfDe15MbConM10_VaSolo_YNingunaOtraParteSupera10Mb()
    {
        await SembrarAsync(maxMb: 10);
        var (lote, items) = await SembrarLoteAsync(4, "ac2");

        await IncluirAsync(lote, items[0], 15 * MiB, 0);
        (await PartesAsync(lote)).Should().Equal([(1, "pendiente", 1, 0, 15 * MiB)], "el PDF mayor que M sale solo y enseguida");

        await IncluirAsync(lote, items[1], 4 * MiB, 1);
        await IncluirAsync(lote, items[2], 4 * MiB, 2);
        await IncluirAsync(lote, items[3], 4 * MiB, 3);
        (await CerrarCarrilAsync(lote)).Aplicado.Should().BeTrue();

        var partes = await PartesAsync(lote);
        partes.Should().Equal([(1, "pendiente", 1, 0, 15 * MiB), (2, "pendiente", 2, 0, 8 * MiB), (3, "pendiente", 1, 0, 4 * MiB)]);
        partes.Skip(1).Should().OnlyContain(p => p.Bytes <= 10 * MiB);
    }

    // ── AC3 ─────────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC3_TodoOmitido_AlTerminarElCarril_UnaUnicaParteSoloConOmitidos()
    {
        await SembrarAsync(maxPdfs: 2);
        var (lote, items) = await SembrarLoteAsync(3, "ac3");
        for (var k = 0; k < 3; k++)
            await OmitirAsync(lote, items[k], k);
        (await PartesAsync(lote)).Should().BeEmpty("los omitidos no llenan partes");

        (await CerrarCarrilAsync(lote)).Should().Be(new CierreCarrilResultado(true, 1, 1));

        (await PartesAsync(lote)).Should().Equal([(1, "pendiente", 0, 3, 0L)]);
        (await ItemsEnParteAsync(lote, 1)).Should().Be(3);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Empaquetando);
    }

    [PostgresFact]
    public async Task AC3_LoteSinItems_ArrancaYSeCierra_ConUnaParteVacia()
    {
        await SembrarAsync();
        var (lote, _) = await SembrarLoteAsync(0, "vacio");

        (await CerrarCarrilAsync(lote)).Should().Be(new CierreCarrilResultado(true, 1, 1));

        (await PartesAsync(lote)).Should().Equal([(1, "pendiente", 0, 0, 0L)]);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Empaquetando);
    }

    [PostgresFact]
    public async Task Omitidos_VanEnLaPrimeraParteQueSale_YLosTardios_EnUnaParteSoloConCsv()
    {
        await SembrarAsync(maxPdfs: 2);
        var (lote, items) = await SembrarLoteAsync(4, "omit");

        await OmitirAsync(lote, items[0], 0);
        await IncluirAsync(lote, items[1], MiB, 1);
        await IncluirAsync(lote, items[2], MiB, 2);
        (await PartesAsync(lote)).Should().Equal([(1, "pendiente", 2, 1, 2 * MiB)]);

        await OmitirAsync(lote, items[3], 3);
        (await CerrarCarrilAsync(lote)).Should().Be(new CierreCarrilResultado(true, 1, 2));

        (await PartesAsync(lote)).Should().Equal([(1, "pendiente", 2, 1, 2 * MiB), (2, "pendiente", 0, 1, 0L)]);
    }

    // ── Guardas ─────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task Guarda_ConItemsVivos_NoCierraElCarril()
    {
        await SembrarAsync();
        var (lote, items) = await SembrarLoteAsync(2, "vivos");
        await IncluirAsync(lote, items[0], MiB, 0);

        (await CerrarCarrilAsync(lote)).Should().Be(CierreCarrilResultado.NoAplicado);

        (await PartesAsync(lote)).Should().BeEmpty();
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.EnProceso);
    }

    [PostgresFact]
    public async Task Guarda_LoteCancelado_NoAsignaPartes_NiEnElCierreDeItem_NiEnElDelCarril()
    {
        await SembrarAsync(maxPdfs: 1);
        var (lote, items) = await SembrarLoteAsync(1, "cancelado", ConsolidadoExportStatus.Cancelado);

        await IncluirAsync(lote, items[0], MiB, 0);
        (await CerrarCarrilAsync(lote)).Should().Be(CierreCarrilResultado.NoAplicado);

        (await PartesAsync(lote)).Should().BeEmpty("con el lote cancelado bajo lock no se asigna (diseño #13307)");
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.Cancelado);
    }

    [PostgresFact]
    public async Task Guarda_SinFilaDeParametros_NoCierraElCarril()
    {
        await SembrarAsync();
        var (lote, _) = await SembrarLoteAsync(0, "sinsettings");
        await ExecAsync("DELETE FROM tramites.consolidado_export_settings");

        (await CerrarCarrilAsync(lote)).Should().Be(CierreCarrilResultado.NoAplicado);
        (await EstadoLoteAsync(lote)).Should().Be(ConsolidadoExportStatus.EnProceso);
    }
}
