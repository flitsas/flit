using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Épica #13216 (HU #13373, ADR-0070 D8) — <see cref="IConsolidadoLoteRepository"/> sobre PostgreSQL.
/// <list type="bullet">
///   <item>Creación en UNA transacción (envuelta en la estrategia de ejecución del contexto): purga del retenido con
///   <c>FOR UPDATE</c>, INSERT del lote, ítems por <c>COPY</c> binario (20.000 filas en una sola ida) y la fila
///   <c>lote_creado</c>. Si cualquiera falla, la transacción se revierte entera.</item>
///   <item>Traducción de errores: 23505 sobre <see cref="IndiceLoteActivo"/> → <see cref="CrearLoteEstado.LoteActivo"/>
///   con el id del activo; cualquier otro → <see cref="CrearLoteEstado.NoCreado"/>. La <c>PostgresException</c>
///   nunca sale de aquí.</item>
///   <item>Las consultas del dueño filtran por <c>requested_by_user_id</c> (RLS decorativa, A3.1); el SQL va
///   parametrizado.</item>
/// </list>
/// Logs sin PII: solo ids y conteos.
/// </summary>
internal sealed partial class ConsolidadoLoteRepository(
    FlitDbContext db,
    ILogger<ConsolidadoLoteRepository>? logger = null) : IConsolidadoLoteRepository
{
    /// <summary>Índice único parcial «un lote activo por usuario» (DDL 133).</summary>
    internal const string IndiceLoteActivo = "uq_consolidado_export_batches_active_per_user";

    private const string UniqueViolation = "23505";

    private static readonly string[] EstadosActivos = [.. ConsolidadoExportStatus.Activos];

    private readonly ILogger _logger = logger ?? NullLogger<ConsolidadoLoteRepository>.Instance;

    public Task<ConsolidadoExportSettings?> ObtenerSettingsAsync(CancellationToken ct = default) =>
        db.ConsolidadoExportSettings.AsNoTracking().FirstOrDefaultAsync(ct);

    public Task<Guid?> ObtenerLoteActivoIdAsync(Guid usuarioId, CancellationToken ct = default) =>
        db.ConsolidadoExportBatches.AsNoTracking()
            .Where(b => b.RequestedByUserId == usuarioId && b.DeletedAt == null && EstadosActivos.Contains(b.Status))
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<CrearLoteResultado> CrearAsync(NuevoLoteConsolidados nuevo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nuevo);
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                nuevo,
                async (_, n, token) => await CrearEnTransaccionAsync(n, token).ConfigureAwait(false),
                verifySucceeded: null,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (EsLoteActivo(ex))
        {
            db.ChangeTracker.Clear();
            var activo = await ObtenerLoteActivoIdAsync(nuevo.UsuarioId, ct).ConfigureAwait(false);
            LogLoteActivo(_logger, nuevo.UsuarioId, activo);
            return activo is { } id
                ? new CrearLoteResultado(CrearLoteEstado.LoteActivo, LoteActivoId: id)
                : new CrearLoteResultado(CrearLoteEstado.NoCreado);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            // Solo el tipo y el SQLSTATE: el mensaje del motor puede citar valores de la fila.
            LogNoCreado(_logger, nuevo.UsuarioId, ex.GetType().Name, BuscarPostgres(ex)?.SqlState);
            return new CrearLoteResultado(CrearLoteEstado.NoCreado);
        }
    }

    public async Task<bool> PurgarAsync(Guid loteId, DateTimeOffset ahora, CancellationToken ct = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            loteId,
            async (_, id, token) =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var lotes = await db.ConsolidadoExportBatches
                    .FromSqlInterpolated($"""
                        SELECT * FROM tramites.consolidado_export_batches
                         WHERE id = {id} AND purged_at IS NULL AND deleted_at IS NULL
                           AND NOT (status = ANY ({EstadosActivos}))
                           FOR UPDATE
                        """)
                    .ToListAsync(token).ConfigureAwait(false);
                if (lotes.Count == 0)
                    return false;

                await PurgarCoreAsync(lotes, ahora, token).ConfigureAwait(false);
                await db.SaveChangesAsync(token).ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
                LogPurgado(_logger, id);
                return true;
            },
            verifySucceeded: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #13376 — una sola sentencia (atómica sin transacción explícita, compatible con la estrategia de reintentos):
    /// <c>lote</c> elige y bloquea el lote por turno, <c>item</c> bloquea su siguiente ítem reclamable y los dos
    /// <c>UPDATE</c> sellan el reclamo. <c>SKIP LOCKED</c> en ambos: otro slot u otra réplica que esté reclamando del
    /// mismo lote pasa al siguiente, nunca espera. Orden de locks lote → ítem, igual que el cierre y la cancelación.
    /// </summary>
    public async Task<ItemLoteReclamado?> ReclamarSiguienteItemAsync(
        string reclamante, int leaseSegundos, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reclamante);
        ArgumentOutOfRangeException.ThrowIfLessThan(leaseSegundos, 1);
        var enCola = ConsolidadoExportStatus.EnCola;
        var enProceso = ConsolidadoExportStatus.EnProceso;
        var pendiente = ConsolidadoExportItemStatus.Pendiente;
        var procesando = ConsolidadoExportItemStatus.Procesando;

        var items = await db.ConsolidadoExportBatchItems
            .FromSqlInterpolated($"""
                WITH lote AS (
                    SELECT b.id
                      FROM tramites.consolidado_export_batches b
                     WHERE b.status IN ({enCola}, {enProceso}) AND b.deleted_at IS NULL
                       AND EXISTS (
                           SELECT 1 FROM tramites.consolidado_export_batch_items r
                            WHERE r.batch_id = b.id
                              AND ((r.status = {pendiente} AND r.next_attempt_at <= now())
                                OR (r.status = {procesando} AND r.lease_until < now())))
                     ORDER BY b.last_claimed_at NULLS FIRST, b.created_at, b.id
                     LIMIT 1
                       FOR UPDATE OF b SKIP LOCKED),
                item AS (
                    SELECT i.id
                      FROM tramites.consolidado_export_batch_items i
                      JOIN lote ON i.batch_id = lote.id
                     WHERE (i.status = {pendiente} AND i.next_attempt_at <= now())
                        OR (i.status = {procesando} AND i.lease_until < now())
                     ORDER BY i.position
                     LIMIT 1
                       FOR UPDATE OF i SKIP LOCKED),
                reclamado AS (
                    UPDATE tramites.consolidado_export_batch_items i
                       SET attempts = CASE WHEN i.status = {procesando} THEN i.attempts + 1 ELSE i.attempts END,
                           status = {procesando},
                           lease_until = now() + make_interval(secs => {leaseSegundos}),
                           claimed_by = {reclamante},
                           updated_at = now()
                      FROM item
                     WHERE i.id = item.id
                    RETURNING i.*),
                sellado AS (
                    UPDATE tramites.consolidado_export_batches b
                       SET last_claimed_at = now(),
                           status = CASE WHEN b.status = {enCola} THEN {enProceso} ELSE b.status END,
                           started_at = COALESCE(b.started_at, now()),
                           updated_at = now()
                      FROM reclamado
                     WHERE b.id = reclamado.batch_id
                    RETURNING b.id)
                SELECT reclamado.* FROM reclamado
                """)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        if (items.Count == 0)
            return null;

        var reclamado = items[0];
        var lote = await db.ConsolidadoExportBatches.AsNoTracking()
            .FirstAsync(b => b.Id == reclamado.BatchId, ct).ConfigureAwait(false);
        return new ItemLoteReclamado(lote, reclamado);
    }

    public async Task<int> IniciarLotesSinItemsAsync(CancellationToken ct = default)
    {
        var enCola = ConsolidadoExportStatus.EnCola;
        var enProceso = ConsolidadoExportStatus.EnProceso;
        var iniciados = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batches b
               SET status = {enProceso},
                   started_at = COALESCE(b.started_at, now()),
                   updated_at = now()
             WHERE b.status = {enCola} AND b.deleted_at IS NULL
               AND NOT EXISTS (SELECT 1 FROM tramites.consolidado_export_batch_items i WHERE i.batch_id = b.id)
            """,
            ct).ConfigureAwait(false);
        if (iniciados > 0)
            LogSinItems(_logger, iniciados);
        return iniciados;
    }

    public async Task<IReadOnlyList<Guid>> ObtenerLotesConCarrilTerminadoAsync(int maximo, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        string[] vivos = [.. ConsolidadoExportItemStatus.Vivos];
        return await db.ConsolidadoExportBatches.AsNoTracking()
            .Where(b => b.Status == ConsolidadoExportStatus.EnProceso && b.DeletedAt == null
                        && !db.ConsolidadoExportBatchItems.Any(i => i.BatchId == b.Id && vivos.Contains(i.Status)))
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .Take(maximo)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private async Task<CrearLoteResultado> CrearEnTransaccionAsync(NuevoLoteConsolidados nuevo, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var ahora = DateTimeOffset.UtcNow;

        // H10a / CF-12: retención de un solo lote por usuario. El lock serializa contra la purga del worker (#13379).
        var retenidos = await db.ConsolidadoExportBatches
            .FromSqlInterpolated($"""
                SELECT * FROM tramites.consolidado_export_batches
                 WHERE requested_by_user_id = {nuevo.UsuarioId} AND purged_at IS NULL AND deleted_at IS NULL
                   AND NOT (status = ANY ({EstadosActivos}))
                   FOR UPDATE
                """)
            .ToListAsync(ct).ConfigureAwait(false);
        if (retenidos.Count > 0)
            await PurgarCoreAsync(retenidos, ahora, ct).ConfigureAwait(false);

        var lote = new ConsolidadoExportBatch
        {
            Id = Guid.CreateVersion7(),
            TenantId = nuevo.TenantId,
            RequestedByUserId = nuevo.UsuarioId,
            RequestedRoleCode = nuevo.RolCodigo,
            ScopeTenantId = nuevo.ScopeTenantId,
            Origin = nuevo.Origen,
            DocumentType = nuevo.TipoDocumento,
            SelectionMode = nuevo.ModoSeleccion,
            Status = ConsolidadoExportStatus.EnCola,
            TotalItems = nuevo.Items.Count,
            DekWrapped = nuevo.DekEnvuelta,
            EffectsAcknowledgedAt = nuevo.EfectosAceptadosEn,
            OtTransitOfficeId = nuevo.OtTransitOfficeId,
            CreatedAt = ahora,
            CreatedBy = nuevo.UsuarioId,
        };
        db.ConsolidadoExportBatches.Add(lote);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await CopiarItemsAsync(lote.Id, nuevo, ct).ConfigureAwait(false);

        db.ConsolidadoExportAuditEntries.Add(new ConsolidadoExportAuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = ahora,
            Event = ConsolidadoExportAuditEvent.LoteCreado,
            Origin = nuevo.Origen,
            BatchId = lote.Id,
            ActorUserId = nuevo.UsuarioId,
            ActorTenantId = nuevo.TenantId,
            ActorRoleCode = nuevo.RolCodigo,
            ScopeTenantId = nuevo.ScopeTenantId,
            ReachedTenantIds = nuevo.Items.Select(i => i.TenantId).Distinct().OrderBy(t => t).ToArray(),
            DocumentType = nuevo.TipoDocumento,
            SelectionMode = nuevo.ModoSeleccion,
            FilterSummary = nuevo.ResumenFiltroJson,
            IdsCount = nuevo.IdsCount,
            ExcludedCount = nuevo.ExcluidosCount,
            TotalItems = nuevo.Items.Count,
            ClientIp = nuevo.ClientIp,
            UserAgent = nuevo.UserAgent,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);

        return new CrearLoteResultado(CrearLoteEstado.Creado, Lote: lote, LotesPurgados: retenidos.Count);
    }

    /// <summary>Ítems por COPY binario en la conexión y transacción del contexto (AC7: 20.000 en ≤ 10 s).</summary>
    private async Task CopiarItemsAsync(Guid loteId, NuevoLoteConsolidados nuevo, CancellationToken ct)
    {
        if (nuevo.Items.Count == 0)
            return;

        var conexion = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var copia = await conexion.BeginBinaryImportAsync(
            """
            COPY tramites.consolidado_export_batch_items
                (id, tenant_id, batch_id, procedure_instance_id, position, reference_number, plate, created_by)
            FROM STDIN (FORMAT BINARY)
            """, ct).ConfigureAwait(false);

        for (var i = 0; i < nuevo.Items.Count; i++)
        {
            var item = nuevo.Items[i];
            await copia.StartRowAsync(ct).ConfigureAwait(false);
            await copia.WriteAsync(Guid.CreateVersion7(), NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
            await copia.WriteAsync(item.TenantId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
            await copia.WriteAsync(loteId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
            await copia.WriteAsync(item.Id, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
            await copia.WriteAsync(i, NpgsqlDbType.Integer, ct).ConfigureAwait(false);
            await copia.WriteAsync(item.ReferenceNumber ?? string.Empty, NpgsqlDbType.Text, ct).ConfigureAwait(false);
            if (item.Plate is null)
                await copia.WriteNullAsync(ct).ConfigureAwait(false);
            else
                await copia.WriteAsync(item.Plate, NpgsqlDbType.Text, ct).ConfigureAwait(false);
            await copia.WriteAsync(nuevo.UsuarioId, NpgsqlDbType.Uuid, ct).ConfigureAwait(false);
        }

        await copia.CompleteAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Núcleo de la purga sobre lotes ya bloqueados: operación de dominio <see cref="ConsolidadoLotePurga"/> + una fila
    /// <c>lote_purgado</c> por lote, a nombre del dueño. No guarda: lo hace la transacción del llamador.
    /// </summary>
    private async Task PurgarCoreAsync(IReadOnlyList<ConsolidadoExportBatch> lotes, DateTimeOffset ahora, CancellationToken ct)
    {
        var ids = lotes.Select(l => l.Id).ToList();
        var partes = await db.ConsolidadoExportBatchParts
            .Where(p => ids.Contains(p.BatchId))
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var lote in lotes)
        {
            ConsolidadoLotePurga.Aplicar(lote, partes.Where(p => p.BatchId == lote.Id), ahora);
            db.ConsolidadoExportAuditEntries.Add(new ConsolidadoExportAuditEntry
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = ahora,
                Event = ConsolidadoExportAuditEvent.LotePurgado,
                Origin = lote.Origin,
                BatchId = lote.Id,
                ActorUserId = lote.RequestedByUserId,
                ActorTenantId = lote.TenantId,
                ActorRoleCode = lote.RequestedRoleCode,
                ScopeTenantId = lote.ScopeTenantId,
                DocumentType = lote.DocumentType,
                TotalItems = lote.TotalItems,
                IncludedCount = lote.IncludedCount,
                OmittedCount = lote.OmittedCount,
                GeneratedCount = lote.GeneratedCount,
            });
        }
    }

    private static bool EsLoteActivo(Exception ex) =>
        BuscarPostgres(ex) is { SqlState: UniqueViolation, ConstraintName: IndiceLoteActivo };

    private static PostgresException? BuscarPostgres(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg)
                return pg;
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Lote de consolidados no creado: el usuario {UsuarioId} ya tiene el lote activo {LoteActivoId} (23505).")]
    private static partial void LogLoteActivo(ILogger logger, Guid usuarioId, Guid? loteActivoId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote de consolidados no creado para el usuario {UsuarioId}: {Tipo} {SqlState}. Transacción revertida.")]
    private static partial void LogNoCreado(ILogger logger, Guid usuarioId, string tipo, string? sqlState);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Carril de ítems: {Lotes} lote(s) sin ítems pasan a en_proceso para su cierre.")]
    private static partial void LogSinItems(ILogger logger, int lotes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Lote de consolidados {LoteId} purgado (borrado criptográfico).")]
    private static partial void LogPurgado(ILogger logger, Guid loteId);
}
