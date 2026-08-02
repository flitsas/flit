using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Destino en V2 de un trámite ya migrado (para enlazar sus adjuntos).</summary>
public sealed record TramiteTarget(Guid V2Id, Guid TenantId);

/// <summary>
/// Todo lo que la libreta sabe de un trámite ya migrado.
/// <para>
/// Lo consume el host HTTP para responder «este trámite ya estaba migrado, en este lote y con
/// este estado» en vez de un silencio que se lea como un no-op. Reintentar una lista de ids
/// siempre fue inofensivo —los loaders devuelven <c>Skipped</c>—, pero sin esto no había forma
/// de distinguir «ya estaba» de «no hice nada».
/// </para>
/// </summary>
public sealed record MigrationMapEntry(
    Guid V2Id,
    Guid TenantId,
    string BatchId,
    string FinalStatus,
    IReadOnlyList<string> Warnings,
    DateTimeOffset MigratedAt);

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

    /// <summary>
    /// La fila completa de <c>migration_map</c>, o <c>null</c> si el trámite nunca se migró.
    /// <para>
    /// Una SOLA consulta escalar: <c>json_build_object(...)::text</c> devuelve un <c>text</c>, que
    /// es el patrón que EF mapea sin fricción (alias <c>"Value"</c>). Un tipo compuesto en
    /// <c>SqlQueryRaw</c> choca con la convención snake_case del DbContext — la misma razón por la
    /// que <see cref="FindTargetAsync"/> usa dos escalares. Aquí serían seis viajes.
    /// </para>
    /// <para>
    /// El cast es <c>::text</c> y no el equivalente <c>#&gt;&gt; '{}'</c> a propósito:
    /// <c>SqlQueryRaw</c> interpreta la plantilla con semántica de <c>string.Format</c>, así que
    /// unas llaves literales en el SQL se toman por un marcador de posición y revientan.
    /// </para>
    /// </summary>
    public async Task<MigrationMapEntry?> FindEntryAsync(
        string v1Table, long v1Id, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT json_build_object(
                           'v2Id',        v2_id,
                           'tenantId',    tenant_id,
                           'batchId',     batch_id,
                           'finalStatus', final_status,
                           'warnings',    warnings,
                           'migratedAt',  migrated_at)::text AS "Value"
                FROM migration.migration_map
                WHERE v1_table = {0} AND v1_id = {1}
                """,
                v1Table, v1Id)
            .ToListAsync(cancellationToken);

        return rows.Count == 0 ? null : JsonSerializer.Deserialize<MigrationMapEntry>(rows[0], EntryJson);
    }

    /// <summary>
    /// Lo mismo que <see cref="FindEntryAsync"/> pero para muchos ids de golpe, indexado por id de
    /// V1. Los que nunca se migraron simplemente no aparecen en el diccionario.
    /// <para>
    /// Existe por la consola web: al recargar la página hay que reconciliar hasta doscientas filas
    /// de un CSV contra la libreta. Una consulta por fila serían doscientos viajes para pintar una
    /// tabla, y el navegador las lanzaría en paralelo contra un host cuyo tope de concurrencia son
    /// dos. Aquí es <c>= ANY</c> sobre la clave primaria: un viaje y un index scan.
    /// </para>
    /// <para>
    /// El parámetro va como <c>long[]</c> y no interpolado en el SQL. Con <c>SqlQueryRaw</c> es
    /// tentador construir la lista a mano —son enteros, "no hay inyección posible"—, pero eso
    /// también convierte cada tamaño de lote en un plan distinto en la caché de Postgres.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyDictionary<long, MigrationMapEntry>> FindEntriesAsync(
        string v1Table, IReadOnlyCollection<long> v1Ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(v1Ids);

        if (v1Ids.Count == 0)
        {
            return new Dictionary<long, MigrationMapEntry>();
        }

        var rows = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT json_build_object(
                           'v1Id',        v1_id,
                           'v2Id',        v2_id,
                           'tenantId',    tenant_id,
                           'batchId',     batch_id,
                           'finalStatus', final_status,
                           'warnings',    warnings,
                           'migratedAt',  migrated_at)::text AS "Value"
                FROM migration.migration_map
                WHERE v1_table = {0} AND v1_id = ANY({1})
                """,
                v1Table, v1Ids.Distinct().ToArray())
            .ToListAsync(cancellationToken);

        var encontrados = new Dictionary<long, MigrationMapEntry>(rows.Count);
        foreach (var row in rows)
        {
            var fila = JsonSerializer.Deserialize<MigrationMapRow>(row, EntryJson);
            if (fila is not null)
            {
                encontrados[fila.V1Id] = fila.Entry;
            }
        }

        return encontrados;
    }

    /// <summary>La misma fila que <see cref="MigrationMapEntry"/> más el id de V1 que la indexa.</summary>
    private sealed record MigrationMapRow(
        long V1Id,
        Guid V2Id,
        Guid TenantId,
        string BatchId,
        string FinalStatus,
        IReadOnlyList<string> Warnings,
        DateTimeOffset MigratedAt)
    {
        internal MigrationMapEntry Entry =>
            new(V2Id, TenantId, BatchId, FinalStatus, Warnings, MigratedAt);
    }

    /// <summary>camelCase para casar con las claves del <c>json_build_object</c> de arriba.</summary>
    private static readonly JsonSerializerOptions EntryJson = new(JsonSerializerDefaults.Web)
    {
        // warnings es jsonb: llega como array real, no como cadena escapada.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

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
