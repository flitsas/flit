using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Destino en V2 de un trámite ya migrado (para enlazar sus adjuntos).</summary>
public sealed record TramiteTarget(Guid V2Id, Guid TenantId);

/// <summary>
/// La "libreta" de la migración: qué registro de V1 quedó como qué registro de V2.
/// <para>
/// Es lo que convierte un script peligroso en un proceso operable. Sin esto no hay
/// idempotencia (re-correr duplicaría), no hay reanudación (un corte a mitad obligaría a
/// empezar de cero) y no hay auditoría (nadie podría demostrar de dónde salió un trámite).
/// </para>
/// </summary>
public sealed class MigrationMapStore(Flit.Infrastructure.Persistence.FlitDbContext db)
{
    /// <summary>
    /// Crea el esquema y la tabla si no existen. Vive en su propio esquema <c>migration</c>
    /// para dejar claro que es andamiaje de migración y no parte del modelo de negocio de V2.
    /// </summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS migration;

            CREATE TABLE IF NOT EXISTS migration.migration_map (
                v1_table     text        NOT NULL,
                v1_id        bigint      NOT NULL,
                v2_table     text        NOT NULL,
                v2_id        uuid        NOT NULL,
                tenant_id    uuid        NOT NULL,
                batch_id     text        NOT NULL,
                final_status varchar(20) NOT NULL,
                warnings     jsonb       NOT NULL DEFAULT '[]',
                migrated_at  timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT pk_migration_map PRIMARY KEY (v1_table, v1_id)
            );

            CREATE INDEX IF NOT EXISTS ix_migration_map_v2_id   ON migration.migration_map (v2_id);
            CREATE INDEX IF NOT EXISTS ix_migration_map_batch   ON migration.migration_map (batch_id);
            """, cancellationToken);

    /// <summary>Id en V2 de un trámite ya migrado, o <c>null</c> si nunca se migró.</summary>
    public async Task<Guid?> FindAsync(string v1Table, long v1Id, CancellationToken cancellationToken)
    {
        var found = await db.Database
            .SqlQueryRaw<Guid>(
                "SELECT v2_id AS \"Value\" FROM migration.migration_map WHERE v1_table = {0} AND v1_id = {1}",
                v1Table, v1Id)
            .ToListAsync(cancellationToken);

        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>
    /// Destino de un trámite ya migrado: su <c>procedure_instance_id</c> y su <c>tenant_id</c>.
    /// Lo necesita la migración de adjuntos (instancia 2), que exige que la data plana ya exista.
    /// </summary>
    public async Task<TramiteTarget?> FindTargetAsync(string v1Table, long v1Id, CancellationToken cancellationToken)
    {
        // Dos consultas escalares con el patrón que EF mapea sin fricción (alias "Value"). Un tipo
        // compuesto en SqlQueryRaw choca con la convención snake_case del DbContext.
        var v2Id = await FindAsync(v1Table, v1Id, cancellationToken);
        if (v2Id is null)
        {
            return null;
        }

        var tenants = await db.Database
            .SqlQueryRaw<Guid>(
                "SELECT tenant_id AS \"Value\" FROM migration.migration_map WHERE v1_table = {0} AND v1_id = {1}",
                v1Table, v1Id)
            .ToListAsync(cancellationToken);

        return new TramiteTarget(v2Id.Value, tenants[0]);
    }

    public async Task RecordAsync(
        string v1Table,
        long v1Id,
        Guid v2Id,
        Guid tenantId,
        string batchId,
        string finalStatus,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO migration.migration_map
                (v1_table, v1_id, v2_table, v2_id, tenant_id, batch_id, final_status, warnings, migrated_at)
            VALUES ({0}, {1}, 'tramites.procedure_instances', {2}, {3}, {4}, {5}, {6}::jsonb, now())
            ON CONFLICT (v1_table, v1_id) DO UPDATE SET
                v2_id = EXCLUDED.v2_id,
                tenant_id = EXCLUDED.tenant_id,
                batch_id = EXCLUDED.batch_id,
                final_status = EXCLUDED.final_status,
                warnings = EXCLUDED.warnings,
                migrated_at = now()
            """,
            [v1Table, v1Id, v2Id, tenantId, batchId, finalStatus,
             System.Text.Json.JsonSerializer.Serialize(warnings)],
            cancellationToken);
    }

    /// <summary>
    /// Borra el trámite de V2 y su rastro en la libreta. Se usa con <c>--force</c> para
    /// re-migrar: borrar y volver a crear es más seguro que intentar un update parcial,
    /// porque el trigger de inmutabilidad impide editar campos fuera de borrador.
    /// <para>
    /// Borra TAMBIÉN los adjuntos de las instancias 2 y 3 y sus filas de
    /// <c>migration_attachment_map</c>. Es imprescindible: al desaparecer el trámite sus adjuntos se
    /// van por cascada, y si la libreta sobreviviera, las instancias 2 y 3 creerían que ya está todo
    /// migrado y no volverían a copiar nada. El trámite se quedaría vacío para siempre y
    /// <b>en silencio</b> — el reporte diría "ya migrados" sin que exista un solo archivo.
    /// </para>
    /// </summary>
    public async Task DeleteMigratedAsync(string v1Table, long v1Id, Guid v2Id, CancellationToken cancellationToken) =>
        // El UPDATE a 'borrador' va primero a propósito: el trigger de inmutabilidad bloquea
        // incluso el DELETE de field_values mientras el padre esté en un estado final.
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE tramites.procedure_instances SET status = 'borrador' WHERE id = {2};
            DELETE FROM tramites.procedure_instance_field_values   WHERE procedure_instance_id = {2};
            DELETE FROM tramites.procedure_instance_actors         WHERE procedure_instance_id = {2};
            DELETE FROM tramites.procedure_instance_status_history WHERE procedure_instance_id = {2};
            DELETE FROM tramites.procedure_instance_attachments    WHERE procedure_instance_id = {2}
                AND source IN ('migration', 'migration_snapshot');
            DELETE FROM tramites.procedure_instances               WHERE id = {2};
            DELETE FROM migration.migration_attachment_map WHERE v1_table = {0} AND v1_id = {1};
            DELETE FROM migration.migration_map           WHERE v1_table = {0} AND v1_id = {1};
            """,
            [v1Table, v1Id, v2Id],
            cancellationToken);
}
