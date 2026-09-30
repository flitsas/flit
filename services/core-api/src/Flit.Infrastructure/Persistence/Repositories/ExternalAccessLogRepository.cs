using Flit.Admin.Domain.Integrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13086 — inserción en <c>integrations.external_access_log</c>. Tabla de plataforma y solo de inserción:
/// SQL parametrizado, sin entidad de EF ni cambio del modelo.
/// </summary>
internal sealed class ExternalAccessLogRepository(FlitDbContext context) : IExternalAccessLogRepository
{
    private const string InsertSql = """
        INSERT INTO integrations.external_access_log
            (client_id, endpoint, request_id, ip, sync_version_from, sync_version_to, items_count,
             tenant_ids, pii_unmasked, http_status, duration_ms, occurred_at)
        VALUES
            (@client_id, @endpoint, @request_id, @ip, @sync_version_from, @sync_version_to, @items_count,
             @tenant_ids, @pii_unmasked, @http_status, @duration_ms, @occurred_at)
        """;

    public Task AddAsync(ExternalAccessLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return context.Database.ExecuteSqlRawAsync(
            InsertSql,
            [
                Param("client_id", NpgsqlDbType.Varchar, entry.ClientId),
                Param("endpoint", NpgsqlDbType.Varchar, entry.Endpoint),
                Param("request_id", NpgsqlDbType.Varchar, entry.RequestId),
                Param("ip", NpgsqlDbType.Inet, entry.Ip),
                Param("sync_version_from", NpgsqlDbType.Bigint, entry.SyncVersionFrom),
                Param("sync_version_to", NpgsqlDbType.Bigint, entry.SyncVersionTo),
                Param("items_count", NpgsqlDbType.Integer, entry.ItemsCount),
                Param("tenant_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, entry.TenantIds?.ToArray()),
                Param("pii_unmasked", NpgsqlDbType.Boolean, entry.PiiUnmasked),
                Param("http_status", NpgsqlDbType.Integer, entry.HttpStatus),
                Param("duration_ms", NpgsqlDbType.Integer, entry.DurationMs),
                Param("occurred_at", NpgsqlDbType.TimestampTz, entry.OccurredAt.ToUniversalTime()),
            ],
            cancellationToken);
    }

    private static NpgsqlParameter Param(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };
}
