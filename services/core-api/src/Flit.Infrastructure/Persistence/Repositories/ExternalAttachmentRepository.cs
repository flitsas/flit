using System.Data;
using System.Data.Common;
using Flit.Tramites.Domain.ExternalSync;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13263 (Feature #13261, Épica #12741) — acceso a datos del envío de adjuntos del cliente externo. SQL
/// parametrizado en UNA transacción que bloquea la fila del trámite (<c>FOR NO KEY UPDATE</c>): dos envíos
/// concurrentes al mismo trámite se serializan y el segundo ve lo que dejó el primero, así que nunca quedan dos
/// adjuntos vigentes de Flit. <c>NO KEY UPDATE</c> (y no <c>UPDATE</c>) para no bloquear los INSERT de filas hijas
/// que otras transacciones hagan sobre el mismo trámite.
///
/// <para>Alcance: el mismo del feed — radicado alguna vez (<c>preasignacion</c>/<c>entregado</c> en el historial), no
/// migrado desde FLIT 1 y sin borrado lógico. Todo lo demás es «no existe».</para>
/// <para>El rol de core-api es dueño de las tablas de <c>tramites</c> y no se le aplica RLS (ver
/// <c>ExternalSyncReadScope</c>); aun así se fija <c>app.current_tenant_id</c> al tenant del trámite, de modo que si
/// algún día RLS se forzara, la escritura sigue siendo la de esa compañía y no de otra.</para>
/// </summary>
internal sealed class ExternalAttachmentRepository(FlitDbContext context) : IExternalAttachmentRepository
{
    private const string LockSql = """
        SELECT pi.tenant_id, pi.status, pi.subsanacion_activa, pi.procedure_type_id, pi.transit_office_id
          FROM tramites.procedure_instances pi
         WHERE pi.id = @procedure_id
           AND pi.is_migrated = false
           AND pi.deleted_at IS NULL
           AND EXISTS (SELECT 1
                         FROM tramites.procedure_instance_status_history h
                        WHERE h.procedure_instance_id = pi.id
                          AND h.to_status IN ('preasignacion', 'entregado'))
           FOR NO KEY UPDATE OF pi
        """;

    private const string VigentesSql = """
        SELECT a.id, a.provider, a.sha256, a.storage_path
          FROM tramites.procedure_instance_attachments a
         WHERE a.procedure_instance_id = @procedure_id
           AND a.tipo = @tipo
           AND a.is_historico = false
         ORDER BY a.uploaded_at DESC, a.id DESC
        """;

    private const string PagadoSql = """
        SELECT EXISTS (SELECT 1
                         FROM tramites.procedure_instance_field_values f
                        WHERE f.procedure_instance_id = @procedure_id
                          AND f.field_key = 'impuesto_departamental_pagado'
                          AND f.value_text = 'true'
                          AND f.source = @source)
        """;

    // Upsert de la marca sobre uq_procedure_instance_field_values_instance_key. form_field_id queda NULL (valor
    // «suelto», DDL 19) como lo escribe «Enviar al OT». No reescribe la fila si la marca ya está vigente.
    private const string MarkPaidSql = """
        INSERT INTO tramites.procedure_instance_field_values
            (tenant_id, procedure_instance_id, field_key, value_text, source, created_at)
        VALUES
            (@tenant_id, @procedure_id, 'impuesto_departamental_pagado', 'true', @source, now())
        ON CONFLICT (procedure_instance_id, field_key) DO UPDATE
           SET value_text = 'true', source = EXCLUDED.source, updated_at = now()
         WHERE procedure_instance_field_values.value_text IS DISTINCT FROM 'true'
            OR procedure_instance_field_values.source IS DISTINCT FROM EXCLUDED.source
        """;

    private const string DeleteSql = """
        DELETE FROM tramites.procedure_instance_attachments
         WHERE procedure_instance_id = @procedure_id AND id = ANY(@ids)
        """;

    private const string InsertSql = """
        INSERT INTO tramites.procedure_instance_attachments
            (tenant_id, procedure_instance_id, tipo, filename, mimetype, size_bytes, sha256, storage_path,
             source, provider, uploaded_at)
        VALUES
            (@tenant_id, @procedure_id, @tipo, @filename, @mimetype, @size_bytes, @sha256, @storage_path,
             'user', @provider, now())
        RETURNING id
        """;

    public async Task<T> RunLockedAsync<T>(
        Guid procedureId,
        string tipo,
        Func<ExternalAttachmentTarget?, IExternalAttachmentWriter, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentException.ThrowIfNullOrEmpty(tipo);

        // El DbContext usa EnableRetryOnFailure: la transacción manual va dentro de la estrategia de ejecución.
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var conn = context.Database.GetDbConnection();
                var tx = transaction.GetDbTransaction();

                var target = await ReadTargetAsync(conn, tx, procedureId, tipo, cancellationToken).ConfigureAwait(false);
                if (target is not null)
                {
                    await context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {target.TenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);
                }

                var result = await work(target, new Writer(conn, tx, target), cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private static async Task<ExternalAttachmentTarget?> ReadTargetAsync(
        DbConnection conn, DbTransaction tx, Guid procedureId, string tipo, CancellationToken cancellationToken)
    {
        Guid tenantId;
        string status;
        bool subsanacion;
        Guid typeId;
        Guid? officeId;

        await using (var cmd = Command(conn, tx, LockSql, ("procedure_id", NpgsqlDbType.Uuid, procedureId)))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            tenantId = reader.GetGuid(0);
            status = reader.GetString(1);
            subsanacion = reader.GetBoolean(2);
            typeId = reader.GetGuid(3);
            officeId = reader.IsDBNull(4) ? null : reader.GetGuid(4);
        }

        var vigentes = new List<ExternalAttachmentExisting>();
        await using (var cmd = Command(conn, tx, VigentesSql,
                   ("procedure_id", NpgsqlDbType.Uuid, procedureId), ("tipo", NpgsqlDbType.Varchar, tipo)))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                vigentes.Add(new ExternalAttachmentExisting(
                    reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        bool pagado;
        await using (var cmd = Command(conn, tx, PagadoSql,
                   ("procedure_id", NpgsqlDbType.Uuid, procedureId),
                   ("source", NpgsqlDbType.Varchar, ExternalAttachmentRules.FieldSource)))
        {
            pagado = (bool)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }

        return new ExternalAttachmentTarget(procedureId, tenantId, status, subsanacion, typeId, officeId, pagado, vigentes);
    }

    private static DbCommand Command(DbConnection conn, DbTransaction tx, string sql, params (string Name, NpgsqlDbType Type, object? Value)[] parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, type, value) in parameters)
        {
            cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
        }

        return cmd;
    }

    private sealed class Writer(DbConnection conn, DbTransaction tx, ExternalAttachmentTarget? target) : IExternalAttachmentWriter
    {
        public async Task<Guid> ReplaceAsync(
            NewExternalAttachment attachment, IReadOnlyCollection<Guid> retire, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            ArgumentNullException.ThrowIfNull(retire);
            var t = target ?? throw new InvalidOperationException("No hay trámite sobre el que escribir.");

            if (retire.Count > 0)
            {
                await using var delete = Command(conn, tx, DeleteSql,
                    ("procedure_id", NpgsqlDbType.Uuid, t.ProcedureId),
                    ("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, retire.ToArray()));
                await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var insert = Command(conn, tx, InsertSql,
                ("tenant_id", NpgsqlDbType.Uuid, t.TenantId),
                ("procedure_id", NpgsqlDbType.Uuid, t.ProcedureId),
                ("tipo", NpgsqlDbType.Varchar, attachment.Tipo),
                ("filename", NpgsqlDbType.Varchar, attachment.Filename),
                ("mimetype", NpgsqlDbType.Varchar, attachment.Mimetype),
                ("size_bytes", NpgsqlDbType.Bigint, attachment.SizeBytes),
                ("sha256", NpgsqlDbType.Varchar, attachment.Sha256),
                ("storage_path", NpgsqlDbType.Varchar, attachment.StoragePath),
                ("provider", NpgsqlDbType.Varchar, ExternalAttachmentRules.Provider));
            try
            {
                return (Guid)(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
            }
            catch (PostgresException ex) when (ExternalAttachmentFirstWins.Is(ex))
            {
                // HU #13265 — el motor lo rechazó (DDL 131): la transacción queda abortada y el handler responde attachment_exists.
                throw new AttachmentFirstWinsConflictException(ex);
            }
        }

        public async Task MarkTaxPaidAsync(CancellationToken cancellationToken)
        {
            var t = target ?? throw new InvalidOperationException("No hay trámite sobre el que escribir.");
            await using var upsert = Command(conn, tx, MarkPaidSql,
                ("tenant_id", NpgsqlDbType.Uuid, t.TenantId),
                ("procedure_id", NpgsqlDbType.Uuid, t.ProcedureId),
                ("source", NpgsqlDbType.Varchar, ExternalAttachmentRules.FieldSource));
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>HU #13265 — reconoce el rechazo del trigger del DDL 131 (también cuando EF lo envuelve en <c>DbUpdateException</c>).</summary>
internal static class ExternalAttachmentFirstWins
{
    public static bool Is(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg
                && pg.SqlState == PostgresErrorCodes.UniqueViolation
                && string.Equals(pg.ConstraintName, ExternalAttachmentRules.FirstWinsConstraint, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
