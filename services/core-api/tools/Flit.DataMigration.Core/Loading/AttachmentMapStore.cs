using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>
/// La "libreta" de los adjuntos: qué columna <c>id_attach*</c> de qué trámite de V1 quedó como qué
/// fila de <c>procedure_instance_attachments</c> en V2, con qué <c>storage_path</c> y qué sha256.
/// Es el equivalente de <see cref="MigrationMapStore"/> para la instancia 2: da idempotencia
/// (re-correr no duplica), trazabilidad (de dónde salió cada binario) y verificación de integridad.
/// </summary>
public sealed class AttachmentMapStore(Flit.Infrastructure.Persistence.FlitDbContext db)
{
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE SCHEMA IF NOT EXISTS migration;

            CREATE TABLE IF NOT EXISTS migration.migration_attachment_map (
                v1_table                 text        NOT NULL,
                v1_id                    bigint      NOT NULL,
                v1_column                text        NOT NULL,
                source_file_id           text        NOT NULL,
                v2_attachment_id         uuid        NOT NULL,
                v2_procedure_instance_id uuid        NOT NULL,
                tenant_id                uuid        NOT NULL,
                tipo                     varchar(40) NOT NULL,
                mode                     text        NOT NULL,
                storage_path             text        NOT NULL,
                sha256                   varchar(64) NOT NULL,
                size_bytes               bigint      NOT NULL,
                filename                 text        NOT NULL,
                mimetype                 varchar(150) NOT NULL,
                batch_id                 text        NOT NULL,
                migrated_at              timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT pk_migration_attachment_map PRIMARY KEY (v1_table, v1_id, v1_column)
            );

            CREATE INDEX IF NOT EXISTS ix_migration_attachment_map_instance
                ON migration.migration_attachment_map (v2_procedure_instance_id);
            CREATE INDEX IF NOT EXISTS ix_migration_attachment_map_batch
                ON migration.migration_attachment_map (batch_id);
            """, cancellationToken);

    /// <summary>Columnas de adjunto ya migradas para un trámite (para saltarlas si no hay <c>--force</c>).</summary>
    public async Task<IReadOnlySet<string>> MigratedColumnsAsync(
        string v1Table, long v1Id, CancellationToken cancellationToken)
    {
        var columns = await db.Database
            .SqlQueryRaw<string>(
                "SELECT v1_column AS \"Value\" FROM migration.migration_attachment_map WHERE v1_table = {0} AND v1_id = {1}",
                v1Table, v1Id)
            .ToListAsync(cancellationToken);

        return columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ids de archivo de V1 ya migrados para un trámite. La instancia 3 los usa para no volver a
    /// guardar un binario que la instancia 2 ya copió: hay piezas que V1 "genera" descargando un
    /// adjunto existente (improntas ya firmadas, certificado de vigencia adjunto), y sin este cotejo
    /// el expediente termina con el mismo documento dos veces.
    /// </summary>
    public async Task<IReadOnlySet<string>> MigratedSourceFileIdsAsync(
        string v1Table, long v1Id, CancellationToken cancellationToken)
    {
        var ids = await db.Database
            .SqlQueryRaw<string>(
                "SELECT source_file_id AS \"Value\" FROM migration.migration_attachment_map WHERE v1_table = {0} AND v1_id = {1}",
                v1Table, v1Id)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task RecordAsync(AttachmentMapEntry entry, string batchId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO migration.migration_attachment_map
                (v1_table, v1_id, v1_column, source_file_id, v2_attachment_id, v2_procedure_instance_id,
                 tenant_id, tipo, mode, storage_path, sha256, size_bytes, filename, mimetype, batch_id, migrated_at)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, now())
            ON CONFLICT (v1_table, v1_id, v1_column) DO UPDATE SET
                source_file_id = EXCLUDED.source_file_id,
                v2_attachment_id = EXCLUDED.v2_attachment_id,
                v2_procedure_instance_id = EXCLUDED.v2_procedure_instance_id,
                tenant_id = EXCLUDED.tenant_id,
                tipo = EXCLUDED.tipo,
                mode = EXCLUDED.mode,
                storage_path = EXCLUDED.storage_path,
                sha256 = EXCLUDED.sha256,
                size_bytes = EXCLUDED.size_bytes,
                filename = EXCLUDED.filename,
                mimetype = EXCLUDED.mimetype,
                batch_id = EXCLUDED.batch_id,
                migrated_at = now()
            """,
            [entry.V1Table, entry.V1Id, entry.V1Column, entry.SourceFileId, entry.V2AttachmentId,
             entry.V2ProcedureInstanceId, entry.TenantId, entry.Tipo, entry.Mode, entry.StoragePath,
             entry.Sha256, entry.SizeBytes, entry.Filename, entry.Mimetype, batchId],
            cancellationToken);
    }

    /// <summary>
    /// Con <c>--force</c> en la instancia 2: borra los adjuntos que ESA instancia metió para este
    /// trámite (nunca toca los nativos ni los de la instancia 3: filtra por
    /// <c>source = 'migration'</c>) y limpia solo SU rastro en la libreta.
    /// <para>
    /// El filtro <c>mode &lt;&gt; 'Snapshot'</c> no es cosmético: las dos instancias comparten
    /// libreta y borrarla entera dejaría los documentos de la instancia 3 en V2 sin registro, con lo
    /// que re-correrla fallaría con violación de clave primaria en vez de saltarlos.
    /// </para>
    /// </summary>
    public async Task DeleteForTramiteAsync(
        string v1Table, long v1Id, Guid procedureInstanceId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM tramites.procedure_instance_attachments
                WHERE procedure_instance_id = {2} AND source = 'migration';
            DELETE FROM migration.migration_attachment_map
                WHERE v1_table = {0} AND v1_id = {1} AND mode <> 'Snapshot';
            """,
            [v1Table, v1Id, procedureInstanceId],
            cancellationToken);

    /// <summary>
    /// Con <c>--force</c> en la instancia 3: borra los documentos materializados por el snapshot y
    /// solo SU rastro en la libreta, dejando intacto lo que copió la instancia 2.
    /// <para>
    /// Sin esto <c>--force</c> era inservible en la instancia 3: saltarse la libreta sin borrar lo
    /// ya escrito hacía que cada pieza chocara contra <c>pk_procedure_instance_attachments</c> (el
    /// id es determinístico sobre la clave de la pieza, así que reintentar produce el mismo id).
    /// </para>
    /// </summary>
    public async Task DeleteSnapshotForTramiteAsync(
        string v1Table, long v1Id, Guid procedureInstanceId, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM tramites.procedure_instance_attachments
                WHERE procedure_instance_id = {2} AND source = 'migration_snapshot';
            DELETE FROM migration.migration_attachment_map
                WHERE v1_table = {0} AND v1_id = {1} AND mode = 'Snapshot';
            """,
            [v1Table, v1Id, procedureInstanceId],
            cancellationToken);
}

/// <summary>Una fila de la libreta de adjuntos.</summary>
public sealed record AttachmentMapEntry
{
    public required string V1Table { get; init; }
    public required long V1Id { get; init; }
    public required string V1Column { get; init; }
    public required string SourceFileId { get; init; }
    public required Guid V2AttachmentId { get; init; }
    public required Guid V2ProcedureInstanceId { get; init; }
    public required Guid TenantId { get; init; }
    public required string Tipo { get; init; }
    public required string Mode { get; init; }
    public required string StoragePath { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public required string Filename { get; init; }
    public required string Mimetype { get; init; }
}
