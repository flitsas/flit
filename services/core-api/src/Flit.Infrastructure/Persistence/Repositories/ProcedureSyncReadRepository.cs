using System.Data;
using System.Data.Common;
using System.Globalization;
using Flit.Infrastructure.Persistence.ExternalSync;
using Flit.Tramites.Domain.ExternalSync;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13076 (Feature #13066, Épica #12737, ADR-0066) — lectura por versión de los trámites de todas
/// las compañías para el feed de sincronización externa, dentro de <see cref="ExternalSyncReadScope"/>.
/// SQL keyset parametrizado (patrón <c>AnalyticsReadRepository</c>), sin concatenar valores.
///
/// <para>Qué entra (contrato v3.1):</para>
/// <list type="bullet">
///   <item>solo trámites radicados al menos una vez: <c>EXISTS</c> en el historial con
///   <c>to_status IN ('preasignacion','entregado')</c>. Una vez dentro no salen aunque retrocedan
///   (nunca <c>draft_finalized_at</c>, que significa otra cosa);</item>
///   <item>nunca los migrados desde FLIT 1 (<c>is_migrated</c>), ni como cambio ni como borrado;</item>
///   <item>solo cambios estables: de transacciones anteriores a la más antigua en curso
///   (<c>pg_snapshot_xmin</c>: por debajo ya no puede aparecer nada nuevo) y con más de
///   <c>StabilityLag</c> de antigüedad (ventana de 5 s del contrato).</item>
/// </list>
/// <para>Orden y cursor: (transacción que selló, versión). Las filas de la asignación inicial no tienen
/// transacción y cuentan como la 0.</para>
/// </summary>
internal sealed class ProcedureSyncReadRepository(FlitDbContext context) : IProcedureSyncReadRepository
{
    public const int MaxPageSize = 1000;

    private const string Select = """
        SELECT pi.id,
               pi.tenant_id,
               COALESCE(pi.sync_xact, '0'::xid8)::text AS sync_xact,
               pi.sync_version,
               pi.sync_changed_at,
               pi.deleted_at IS NOT NULL AS is_deleted
          FROM tramites.procedure_instances pi
         WHERE pi.is_migrated = false
           AND COALESCE(pi.sync_xact, '0'::xid8) < pg_snapshot_xmin(pg_current_snapshot())
           AND pi.sync_changed_at <= clock_timestamp() - @lag
           AND EXISTS (SELECT 1
                         FROM tramites.procedure_instance_status_history h
                        WHERE h.procedure_instance_id = pi.id
                          AND h.to_status IN ('preasignacion', 'entregado'))
        """;

    private const string OrderAndLimit = """

         ORDER BY COALESCE(pi.sync_xact, '0'::xid8), pi.sync_version
         LIMIT @limit
        """;

    private const string FromStartSql = Select + OrderAndLimit;

    private const string AfterCursorSql = Select + """

           AND (COALESCE(pi.sync_xact, '0'::xid8), pi.sync_version) > (CAST(@after_xact AS xid8), @after_version)
        """ + OrderAndLimit;

    private const string SinceSql = Select + """

           AND pi.sync_changed_at >= @since
        """ + OrderAndLimit;

    public Task<IReadOnlyList<ProcedureSyncChange>> ReadChangesAsync(
        ProcedureSyncPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.After is not null && request.Since is not null)
        {
            throw new ArgumentException("El cursor y la fecha de arranque son excluyentes.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(request.PageSize, 1, nameof(request));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.PageSize, MaxPageSize, nameof(request));
        ArgumentOutOfRangeException.ThrowIfLessThan(request.StabilityLag, TimeSpan.Zero, nameof(request));

        return new ExternalSyncReadScope(context).ExecuteAsync(
            (conn, tx) => ReadAsync(conn, tx, request, cancellationToken), cancellationToken);
    }

    private static async Task<IReadOnlyList<ProcedureSyncChange>> ReadAsync(
        DbConnection conn, DbTransaction tx, ProcedureSyncPageRequest request, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        if (request.After is { } after)
        {
            cmd.CommandText = AfterCursorSql;
            AddParam(cmd, "after_xact", DbType.String, after.Transaction.ToString(CultureInfo.InvariantCulture));
            AddParam(cmd, "after_version", DbType.Int64, after.Version);
        }
        else if (request.Since is { } since)
        {
            cmd.CommandText = SinceSql;
            AddParam(cmd, "since", DbType.DateTimeOffset, since.ToUniversalTime());
        }
        else
        {
            cmd.CommandText = FromStartSql;
        }

        AddParam(cmd, "lag", DbType.Object, request.StabilityLag);
        AddParam(cmd, "limit", DbType.Int32, request.PageSize);

        var changes = new List<ProcedureSyncChange>(request.PageSize);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            changes.Add(new ProcedureSyncChange(
                reader.GetGuid(0),
                reader.GetGuid(1),
                new ProcedureSyncPosition(
                    ulong.Parse(reader.GetString(2), CultureInfo.InvariantCulture),
                    reader.GetInt64(3)),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetBoolean(5)));
        }

        return changes;
    }

    private static void AddParam(DbCommand cmd, string name, DbType type, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        if (type != DbType.Object)
        {
            p.DbType = type;
        }

        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
