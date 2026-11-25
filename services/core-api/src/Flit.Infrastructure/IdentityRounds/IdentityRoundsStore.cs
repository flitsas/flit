using System.Data;
using System.Data.Common;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.Identity.Rounds;
using Flit.Tramites.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.IdentityRounds;

/// <summary>
/// <see cref="IIdentityRoundsStore"/> sobre <c>admin.identity_rounds_config</c> (fila única, <c>id = 1</c>) y
/// <c>admin.identity_rounds_alert_audit</c> (<c>UNIQUE (validation_id, ronda)</c>). SQL parametrizado, sin entidades EF: son tablas
/// globales de plataforma (sin RLS, como <c>admin.ia_consumo_log</c>) y evitan tocar el modelo/snapshot. Si el contexto tiene una
/// transacción abierta (webhook, worker de reconciliación) los comandos la usan.
/// </summary>
internal sealed class IdentityRoundsStore(FlitDbContext db) : IIdentityRoundsStore
{
    public async Task<IdentityRoundsConfig> GetConfigAsync(CancellationToken ct = default)
    {
        const string sql = "SELECT rondas_maximas, disparador_alerta, correos_alerta FROM admin.identity_rounds_config WHERE id = 1";
        return await WithCommandAsync(sql, _ => { }, async cmd =>
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                return IdentityRoundsConfig.Default;

            var correos = reader.IsDBNull(2) ? [] : (string[])reader.GetValue(2);
            return IdentityRoundsConfig.Create(reader.GetInt32(0), reader.GetString(1), correos);
        }, ct).ConfigureAwait(false);
    }

    public Task SaveConfigAsync(IdentityRoundsConfig config, Guid? updatedBy, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        const string sql = """
            INSERT INTO admin.identity_rounds_config (id, rondas_maximas, disparador_alerta, correos_alerta, updated_at, updated_by)
            VALUES (1, @rondas, @disparador, @correos, @now, @by)
            ON CONFLICT (id) DO UPDATE
               SET rondas_maximas = EXCLUDED.rondas_maximas,
                   disparador_alerta = EXCLUDED.disparador_alerta,
                   correos_alerta = EXCLUDED.correos_alerta,
                   updated_at = EXCLUDED.updated_at,
                   updated_by = EXCLUDED.updated_by
            """;
        return WithCommandAsync(sql, cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("rondas", NpgsqlDbType.Integer) { Value = config.RondasMaximas });
            cmd.Parameters.Add(new NpgsqlParameter("disparador", NpgsqlDbType.Text) { Value = config.DisparadorAlerta });
            cmd.Parameters.Add(new NpgsqlParameter("correos", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = config.CorreosAlerta.ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() });
            cmd.Parameters.Add(new NpgsqlParameter("by", NpgsqlDbType.Uuid) { Value = (object?)updatedBy ?? DBNull.Value });
        }, async cmd => await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), ct);
    }

    public async Task<int> CountRoundsForPersonAsync(Guid tenantId, string documentType, string documentNumber, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(*)::int + COALESCE(SUM(resend_count), 0)::int
            FROM tramites.procedure_instance_biometric_validations
            WHERE tenant_id = 
              AND upper(btrim(document_type)) = upper(btrim())
              AND upper(btrim(document_number)) = upper(btrim())
            """;
        var total = await WithCommandAsync(sql, cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenantId });
            cmd.Parameters.Add(new NpgsqlParameter("tipo", NpgsqlDbType.Text) { Value = documentType });
            cmd.Parameters.Add(new NpgsqlParameter("documento", NpgsqlDbType.Text) { Value = documentNumber });
        }, async cmd => Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0, System.Globalization.CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        return total;
    }

    public async Task<bool> TryRegisterAlertAsync(IdentityRoundsAlertRecord alert, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        const string sql = """
            INSERT INTO admin.identity_rounds_alert_audit
                (id, validation_id, procedure_instance_id, ronda, disparador, destinatarios, fecha, resultado)
            VALUES (@id, @validation, @instance, @ronda, @disparador, @destinatarios, @fecha, @resultado)
            ON CONFLICT (validation_id, ronda) DO NOTHING
            """;
        var inserted = await WithCommandAsync(sql, cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = alert.Id });
            cmd.Parameters.Add(new NpgsqlParameter("validation", NpgsqlDbType.Uuid) { Value = alert.ValidationId });
            cmd.Parameters.Add(new NpgsqlParameter("instance", NpgsqlDbType.Uuid) { Value = (object?)alert.ProcedureInstanceId ?? DBNull.Value });
            cmd.Parameters.Add(new NpgsqlParameter("ronda", NpgsqlDbType.Integer) { Value = alert.Ronda });
            cmd.Parameters.Add(new NpgsqlParameter("disparador", NpgsqlDbType.Text) { Value = alert.Disparador });
            cmd.Parameters.Add(new NpgsqlParameter("destinatarios", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = alert.Destinatarios.ToArray() });
            cmd.Parameters.Add(new NpgsqlParameter("fecha", NpgsqlDbType.TimestampTz) { Value = alert.Fecha.ToUniversalTime() });
            cmd.Parameters.Add(new NpgsqlParameter("resultado", NpgsqlDbType.Text) { Value = alert.Resultado });
        }, async cmd => await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return inserted == 1;
    }

    public Task CompleteAlertAsync(Guid alertId, string resultado, CancellationToken ct = default)
    {
        const string sql = "UPDATE admin.identity_rounds_alert_audit SET resultado = @resultado WHERE id = @id";
        return WithCommandAsync(sql, cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = alertId });
            cmd.Parameters.Add(new NpgsqlParameter("resultado", NpgsqlDbType.Text) { Value = resultado });
        }, async cmd => await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false), ct);
    }

    private async Task<T> WithCommandAsync<T>(
        string sql, Action<DbCommand> bind, Func<DbCommand, Task<T>> run, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            bind(cmd);
            return await run(cmd).ConfigureAwait(false);
        }
        finally
        {
            if (openedHere)
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
