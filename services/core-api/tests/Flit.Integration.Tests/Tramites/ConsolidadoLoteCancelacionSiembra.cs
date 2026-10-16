using System.Net;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13386 — siembra común (SQL parametrizado) de las pruebas de cancelación contra PostgreSQL: compañía, usuario,
/// trámites, lote, ítems, partes y parámetros del motor; y la cancelación real de #13385. Datos sintéticos (sin PII).
/// </summary>
/// <remarks>Uso de ejemplo: <c>var lote = await LoteAsync(u, ConsolidadoExportStatus.EnProceso, total: 2);</c>.</remarks>
public abstract class ConsolidadoLoteCancelacionSiembra(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    protected static readonly Guid Compania = new("b13386a0-0000-7000-8000-000000013386");

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected async Task ExecAsync(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn) { CommandTimeout = 120 };
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    protected async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] ps)
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (n, v) in ps)
            cmd.Parameters.AddWithValue(n, v);
        return (T)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>Compañía + parámetros del motor (truncados en el reset: configuración, no catálogo).</summary>
    protected async Task SembrarAsync(int maxPdfs = 1)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-CIA-13386", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        await ExecAsync(
            """
            DELETE FROM tramites.consolidado_export_settings;
            INSERT INTO tramites.consolidado_export_settings (id, is_active, max_pdfs_per_part, max_mb_per_part)
            VALUES (uuidv7(), true, @n, 250);
            """,
            ("n", maxPdfs));
    }

    protected async Task<Guid> UsuarioAsync(string sufijo)
    {
        var u = Guid.CreateVersion7();
        await ExecAsync(
            "INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES (@u, @e, 'Usuario lote', 'active', now())",
            ("u", u), ("e", $"lote13386-{sufijo}@it.test"));
        return u;
    }

    protected async Task<List<Guid>> TramitesAsync(Guid usuario, int n, string prefijo)
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
            ("c", Compania), ("u", usuario), ("p", prefijo), ("ids", ids));
        return [.. ids];
    }

    /// <summary>Lote del usuario; terminal ⇒ <c>finished_at</c>/<c>expires_at</c>. <paramref name="minutosAtras"/> fija <c>created_at</c>.</summary>
    protected async Task<Guid> LoteAsync(
        Guid usuario, string estado = ConsolidadoExportStatus.EnProceso, int total = 0, int incluidos = 0, int generados = 0,
        int minutosAtras = 0, short partes = 0)
    {
        var lote = Guid.CreateVersion7();
        var terminal = !ConsolidadoExportStatus.EsActivo(estado);
        await ExecAsync(
            $"""
            INSERT INTO tramites.consolidado_export_batches
              (id, tenant_id, requested_by_user_id, requested_role_code, origin, document_type, selection_mode, status,
               total_items, included_count, generated_count, parts_count, dek_wrapped, effects_acknowledged_at, created_by,
               created_at, started_at, finished_at, expires_at)
            VALUES (@l, @c, @u, 'Radicador', 'tramites', 'consolidado', 'ids', @s, @t, @i, @g, @p,
                    '\x010203'::bytea, now(), @u, now() - make_interval(mins => @m), now(),
                    {(terminal ? "now(), now() + interval '23 hours'" : "NULL, NULL")});
            """,
            ("l", lote), ("c", Compania), ("u", usuario), ("s", estado), ("t", total), ("i", incluidos), ("g", generados),
            ("m", minutosAtras), ("p", partes));
        return lote;
    }

    /// <summary>Ítems: <c>pendiente</c>, <c>procesando</c> (lease vigente, <c>slot-1</c>) o <c>incluido</c> (snapshot, parte opcional).</summary>
    protected async Task<Guid[]> ItemsAsync(Guid lote, Guid usuario, IReadOnlyList<Guid> tramites, string estado, int desde = 0, short? parte = null)
    {
        var ids = tramites.Select(_ => Guid.CreateVersion7()).ToArray();
        await ExecAsync(
            """
            INSERT INTO tramites.consolidado_export_batch_items
                (id, tenant_id, batch_id, procedure_instance_id, position, status, lease_until, claimed_by, reference_number,
                 attachment_id, storage_path, size_bytes, delivery_mode, processed_at, part_number, created_by)
            SELECT i.id, @c, @l, t.id, @desde + t.n - 1, @s,
                   CASE WHEN @s = 'procesando' THEN now() + interval '10 minutes' END,
                   CASE WHEN @s = 'procesando' THEN 'slot-1' END,
                   'IT-13386-' || (@desde + t.n),
                   CASE WHEN @s = 'incluido' THEN gen_random_uuid() END,
                   CASE WHEN @s = 'incluido' THEN 'fm/consolidado-' || t.n END,
                   CASE WHEN @s = 'incluido' THEN 100 END,
                   CASE WHEN @s = 'incluido' THEN 'generado' END,
                   CASE WHEN @s = 'incluido' THEN now() END,
                   CASE WHEN @s = 'incluido' THEN NULLIF(@parte, 0::smallint) END,
                   @u
              FROM unnest(@tram) WITH ORDINALITY AS t(id, n)
              JOIN unnest(@ids) WITH ORDINALITY AS i(id, n) ON i.n = t.n
            """,
            ("c", Compania), ("l", lote), ("u", usuario), ("s", estado), ("desde", desde),
            ("parte", parte ?? (short)0), ("tram", tramites.ToArray()), ("ids", ids));
        return ids;
    }

    protected async Task ParteAsync(Guid lote, short numero, string estado)
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

    protected Task<long> AuditoriasAsync(Guid lote, string evento) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_audit WHERE batch_id = @l AND event = @e", ("l", lote), ("e", evento));

    protected Task<string> EstadoLoteAsync(Guid lote) => ScalarAsync<string>(
        "SELECT status FROM tramites.consolidado_export_batches WHERE id = @l", ("l", lote));

    protected Task<long> PartesAsync(Guid lote) => ScalarAsync<long>(
        "SELECT count(*) FROM tramites.consolidado_export_batch_parts WHERE batch_id = @l", ("l", lote));

    protected async Task<CancelarLoteResultado> CancelarAsync(Guid lote, Guid usuario)
    {
        await using var ctx = NewContext();
        return await new ConsolidadoLoteRepository(ctx).CancelarAsync(
            new CancelacionLote(lote, usuario, "Radicador", IPAddress.Parse("10.13.38.6"), "ua/13386"), Ct);
    }
}
