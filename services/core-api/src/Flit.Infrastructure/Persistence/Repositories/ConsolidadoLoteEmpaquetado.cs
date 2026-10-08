using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13378 (Épica #13216, ADR-0070 D2/D4/D8) — <see cref="IConsolidadoLoteEmpaquetado"/> sobre PostgreSQL.
/// <list type="bullet">
///   <item><b>Reclamo</b>: una sola sentencia (CTE lote → parte, <c>FOR UPDATE SKIP LOCKED</c> en ese orden), como el
///   reclamo de ítems de #13376. Lotes <c>en_proceso</c> (partes llenas por N/M) y <c>empaquetando</c> (la última).</item>
///   <item><b>Escrituras</b>: cada una en una transacción corta dentro de la estrategia de reintentos del contexto, que
///   empieza por <c>SELECT … FOR UPDATE</c> del lote (orden lote → parte/ítem, el mismo del cierre de ítems, el cierre
///   del carril y la cancelación #13385). Con el lock tomado, el estado del lote decide: si ya no está
///   <c>en_proceso</c>/<c>empaquetando</c> (cancelado, terminal o borrado) la parte queda <c>descartada</c>.</item>
///   <item><b>Cierre y fallo condicionados</b> a <c>status = 'empaquetando' AND attempts = intentos del reclamo</c>.</item>
///   <item><b>Transición terminal</b> (<c>completado</c>, <c>completado_con_omitidos</c>, <c>fallido</c>) con
///   <c>finished_at</c>, <c>expires_at = finished_at + retention_hours</c> y la fila <c>lote_finalizado</c> en la misma
///   transacción: si la auditoría falla, no hay transición. El cupo «un lote activo por usuario» (índice único parcial
///   sobre los estados activos) se libera con el mismo <c>UPDATE</c>.</item>
/// </list>
/// SQL parametrizado; sin logs con datos personales (no escribe logs: los registra el carril con ids).
/// </summary>
/// <remarks>Uso de ejemplo: <c>var r = await new ConsolidadoLoteEmpaquetado(db).ReclamarSiguienteParteAsync(900, ct);</c>.</remarks>
public sealed class ConsolidadoLoteEmpaquetado(FlitDbContext db) : IConsolidadoLoteEmpaquetado
{
    /// <summary>Retención por defecto del DDL 133 si faltara la fila de parámetros (el CHECK exige <c>expires_at</c>).</summary>
    internal const int RetencionPorDefectoHoras = 24;

    private const string Borrado = "__borrado__";
    private const string ParteEmpaquetando = ConsolidadoExportPartStatus.Empaquetando;
    private const string PartePendiente = ConsolidadoExportPartStatus.Pendiente;

    public async Task<ParteLoteReclamada?> ReclamarSiguienteParteAsync(int leaseSegundos, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(leaseSegundos, 1);
        var enProceso = ConsolidadoExportStatus.EnProceso;
        var empaquetando = ConsolidadoExportStatus.Empaquetando;

        var partes = await db.ConsolidadoExportBatchParts
            .FromSqlInterpolated($"""
                WITH lote AS (
                    SELECT b.id
                      FROM tramites.consolidado_export_batches b
                     WHERE b.status IN ({enProceso}, {empaquetando}) AND b.deleted_at IS NULL
                       AND EXISTS (
                           SELECT 1 FROM tramites.consolidado_export_batch_parts r
                            WHERE r.batch_id = b.id
                              AND (r.status = {PartePendiente}
                                OR (r.status = {ParteEmpaquetando} AND r.lease_until < now())))
                     ORDER BY b.created_at, b.id
                     LIMIT 1
                       FOR UPDATE OF b SKIP LOCKED),
                parte AS (
                    SELECT p.id
                      FROM tramites.consolidado_export_batch_parts p
                      JOIN lote ON p.batch_id = lote.id
                     WHERE p.status = {PartePendiente}
                        OR (p.status = {ParteEmpaquetando} AND p.lease_until < now())
                     ORDER BY p.part_number
                     LIMIT 1
                       FOR UPDATE OF p SKIP LOCKED)
                UPDATE tramites.consolidado_export_batch_parts p
                   SET attempts = CASE WHEN p.status = {ParteEmpaquetando} THEN p.attempts + 1 ELSE p.attempts END,
                       status = {ParteEmpaquetando},
                       lease_until = now() + make_interval(secs => {leaseSegundos}),
                       updated_at = now()
                  FROM parte
                 WHERE p.id = parte.id
                RETURNING p.*
                """)
            .AsNoTracking()
            .ToListAsync(ct).ConfigureAwait(false);
        if (partes.Count == 0)
            return null;

        var reclamada = partes[0];
        var lote = await db.ConsolidadoExportBatches.AsNoTracking()
            .FirstAsync(b => b.Id == reclamada.BatchId, ct).ConfigureAwait(false);
        return new ParteLoteReclamada(lote, reclamada);
    }

    public async Task<ContenidoParteLote> LeerContenidoAsync(Guid loteId, short partNumber, CancellationToken ct = default)
    {
        var incluido = ConsolidadoExportItemStatus.Incluido;
        var omitido = ConsolidadoExportItemStatus.Omitido;
        var deLaParte = db.ConsolidadoExportBatchItems.AsNoTracking()
            .Where(i => i.BatchId == loteId && i.PartNumber == partNumber);

        var pdfs = await deLaParte
            .Where(i => i.Status == incluido)
            .OrderBy(i => i.ProcessedAt).ThenBy(i => i.Position)
            .Select(i => new PdfDeParte(
                i.Id, i.TenantId, i.ProcedureInstanceId, i.Position, i.ReferenceNumber, i.Plate, i.StoragePath!))
            .ToListAsync(ct).ConfigureAwait(false);
        var omitidos = await deLaParte
            .Where(i => i.Status == omitido)
            .OrderBy(i => i.Position)
            .Select(i => new OmitidoDeParte(i.Id, i.Position, i.ReferenceNumber, i.Plate, i.OmissionReason))
            .ToListAsync(ct).ConfigureAwait(false);
        return new ContenidoParteLote(pdfs, omitidos);
    }

    public Task<bool> DescartarSiLoteInactivoAsync(Guid loteId, short partNumber, short intentos, CancellationToken ct = default) =>
        EnTransaccionAsync(
            async token =>
            {
                if (EsVivo(await BloquearLoteAsync(loteId, token).ConfigureAwait(false)))
                    return false;
                await DescartarAsync(loteId, partNumber, token).ConfigureAwait(false);
                return true;
            },
            ct);

    public Task<CierreParteDesenlace> CerrarParteAsync(CierreParteLote cierre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cierre);
        ArgumentNullException.ThrowIfNull(cierre.Almacenado);
        return EnTransaccionAsync(
            async token =>
            {
                if (!EsVivo(await BloquearLoteAsync(cierre.LoteId, token).ConfigureAwait(false)))
                {
                    await DescartarAsync(cierre.LoteId, cierre.PartNumber, token).ConfigureAwait(false);
                    return CierreParteDesenlace.Descartada;
                }

                Guid[] noDisponibles = [.. cierre.ItemsNoDisponibles.Distinct()];
                var degradados = noDisponibles.Length;
                var cerrada = ConsolidadoExportPartStatus.Cerrada;
                var almacenado = cierre.Almacenado;
                var filas = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE tramites.consolidado_export_batch_parts
                       SET status = {cerrada},
                           lease_until = NULL,
                           plain_size_bytes = {cierre.BytesEnClaro},
                           stored_size_bytes = {almacenado.SizeBytes},
                           stored_sha256 = {almacenado.Sha256},
                           storage_path = {almacenado.StoragePath},
                           closed_at = now(),
                           pdf_count = pdf_count - {degradados},
                           omitted_count = omitted_count + {degradados},
                           updated_at = now()
                     WHERE batch_id = {cierre.LoteId} AND part_number = {cierre.PartNumber}
                       AND status = {ParteEmpaquetando} AND attempts = {cierre.IntentosReclamados}
                    """,
                    token).ConfigureAwait(false);
                if (filas != 1)
                    return CierreParteDesenlace.NoAplicada;

                if (degradados > 0)
                    await DegradarNoDisponiblesAsync(cierre.LoteId, cierre.PartNumber, noDisponibles, token).ConfigureAwait(false);
                return CierreParteDesenlace.Cerrada;
            },
            ct);
    }

    public Task<FalloParteDesenlace> RegistrarFalloParteAsync(FalloParteLote fallo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fallo);
        return EnTransaccionAsync(
            async token =>
            {
                if (!EsVivo(await BloquearLoteAsync(fallo.LoteId, token).ConfigureAwait(false)))
                {
                    await DescartarAsync(fallo.LoteId, fallo.PartNumber, token).ConfigureAwait(false);
                    return FalloParteDesenlace.Descartada;
                }

                var agotada = fallo.Intentos >= fallo.MaxIntentos;
                var estado = agotada ? ConsolidadoExportPartStatus.Fallida : PartePendiente;
                var filas = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE tramites.consolidado_export_batch_parts
                       SET status = {estado},
                           attempts = {fallo.Intentos},
                           lease_until = NULL,
                           updated_at = now()
                     WHERE batch_id = {fallo.LoteId} AND part_number = {fallo.PartNumber}
                       AND status = {ParteEmpaquetando} AND attempts = {fallo.IntentosReclamados}
                    """,
                    token).ConfigureAwait(false);
                if (filas != 1)
                    return FalloParteDesenlace.NoAplicada;
                if (!agotada)
                    return FalloParteDesenlace.Reprogramada;

                await FallarCoreAsync(fallo.LoteId, ConsolidadoLoteErrores.ParteIntentosAgotados, token).ConfigureAwait(false);
                return FalloParteDesenlace.LoteFallido;
            },
            ct);
    }

    public Task<bool> FallarLoteAsync(Guid loteId, string codigoError, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoError);
        return EnTransaccionAsync(
            async token =>
            {
                var estado = await BloquearLoteAsync(loteId, token).ConfigureAwait(false);
                if (estado is null || !ConsolidadoExportStatus.EsActivo(estado))
                    return false;
                await FallarCoreAsync(loteId, codigoError, token).ConfigureAwait(false);
                return true;
            },
            ct);
    }

    public async Task<IReadOnlyList<Guid>> ObtenerLotesParaFinalizarAsync(int maximo, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        return await ListosParaFinalizar(db.ConsolidadoExportBatches.AsNoTracking())
            .OrderBy(b => b.CreatedAt)
            .Select(b => b.Id)
            .Take(maximo)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public Task<LoteFinalizado?> FinalizarLoteAsync(Guid loteId, CancellationToken ct = default) =>
        EnTransaccionAsync(
            async token =>
            {
                var empaquetando = ConsolidadoExportStatus.Empaquetando;
                var lotes = await db.ConsolidadoExportBatches
                    .FromSqlInterpolated($"""
                        SELECT * FROM tramites.consolidado_export_batches
                         WHERE id = {loteId} AND status = {empaquetando} AND deleted_at IS NULL
                           FOR UPDATE
                        """)
                    .ToListAsync(token).ConfigureAwait(false);
                if (lotes.Count == 0)
                    return null;
                if (!await ListosParaFinalizar(db.ConsolidadoExportBatches.AsNoTracking().Where(b => b.Id == loteId))
                        .AnyAsync(token).ConfigureAwait(false))
                    return null;

                var lote = lotes[0];
                var estado = lote.OmittedCount == 0
                    ? ConsolidadoExportStatus.Completado
                    : ConsolidadoExportStatus.CompletadoConOmitidos;
                return await TerminarAsync(lote, estado, codigoError: null, destruirDek: false, token).ConfigureAwait(false);
            },
            ct);

    // ── Núcleo ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lote <c>empaquetando</c>, no borrado, con al menos una parte, todas <c>cerrada</c>, y sin ítems vivos. Una parte
    /// <c>fallida</c> nunca llega aquí: su fallo ya dejó el lote en <c>fallido</c>.
    /// </summary>
    private IQueryable<ConsolidadoExportBatch> ListosParaFinalizar(IQueryable<ConsolidadoExportBatch> lotes)
    {
        string[] vivos = [.. ConsolidadoExportItemStatus.Vivos];
        var cerrada = ConsolidadoExportPartStatus.Cerrada;
        return lotes.Where(b => b.Status == ConsolidadoExportStatus.Empaquetando && b.DeletedAt == null
            && db.ConsolidadoExportBatchParts.Any(p => p.BatchId == b.Id)
            && !db.ConsolidadoExportBatchParts.Any(p => p.BatchId == b.Id && p.Status != cerrada)
            && !db.ConsolidadoExportBatchItems.Any(i => i.BatchId == b.Id && vivos.Contains(i.Status)));
    }

    /// <summary>
    /// <c>fallido</c> bajo el lock ya tomado: partes sin cerrar <c>descartada</c>, ítems vivos <c>cancelado</c> (el cierre
    /// en vuelo de un ítem, <c>WHERE status = 'procesando'</c>, ya no escribe), DEK destruida y <c>lote_finalizado</c>.
    /// </summary>
    private async Task FallarCoreAsync(Guid loteId, string codigoError, CancellationToken ct)
    {
        var descartada = ConsolidadoExportPartStatus.Descartada;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batch_parts
               SET status = {descartada}, lease_until = NULL, updated_at = now()
             WHERE batch_id = {loteId} AND status IN ({PartePendiente}, {ParteEmpaquetando})
            """,
            ct).ConfigureAwait(false);

        var cancelado = ConsolidadoExportItemStatus.Cancelado;
        var pendiente = ConsolidadoExportItemStatus.Pendiente;
        var procesando = ConsolidadoExportItemStatus.Procesando;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batch_items
               SET status = {cancelado}, lease_until = NULL, updated_at = now()
             WHERE batch_id = {loteId} AND status IN ({pendiente}, {procesando})
            """,
            ct).ConfigureAwait(false);

        var lote = await db.ConsolidadoExportBatches.FirstAsync(b => b.Id == loteId, ct).ConfigureAwait(false);
        await TerminarAsync(lote, ConsolidadoExportStatus.Fallido, codigoError, destruirDek: true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Transición terminal de un lote ya bloqueado y cargado con seguimiento: estado, <c>finished_at</c>,
    /// <c>expires_at</c> y la fila <c>lote_finalizado</c> con los conteos finales y las partes, en un solo <c>SaveChanges</c>.
    /// </summary>
    private async Task<LoteFinalizado> TerminarAsync(
        ConsolidadoExportBatch lote, string estado, string? codigoError, bool destruirDek, CancellationToken ct)
    {
        var retencion = await db.ConsolidadoExportSettings.AsNoTracking()
            .Select(s => (int?)s.RetentionHours)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? RetencionPorDefectoHoras;

        // Precisión de microsegundos (timestamptz): finished_at y occurred_at quedan idénticos en la base.
        var ahora = DateTimeOffset.UtcNow;
        ahora = new DateTimeOffset(ahora.Ticks - (ahora.Ticks % 10), TimeSpan.Zero);

        lote.Status = estado;
        lote.FinishedAt = ahora;
        lote.ExpiresAt = ahora.AddHours(retencion);
        lote.UpdatedAt = ahora;
        if (codigoError is not null)
            lote.ErrorCode = codigoError;
        if (destruirDek)
            lote.DekWrapped = null;

        db.ConsolidadoExportAuditEntries.Add(new ConsolidadoExportAuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = ahora,
            Event = ConsolidadoExportAuditEvent.LoteFinalizado,
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
            // AC2: «y partes» — las partes en que se dividió el lote (batches.parts_count). En un fallido (AC3) cuenta
            // también las descartadas/fallidas (0 si falló antes de crear ninguna): que no son descargables lo dicen el
            // estado y la DEK destruida, no este conteo.
            PartsCount = lote.PartsCount,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LoteFinalizado(lote.Id, estado, ahora, lote.ExpiresAt.Value);
    }

    /// <summary>
    /// AC4 — incluidos sin PDF legible pasan a <c>omitido adjunto_no_disponible</c>; el lote resta incluidos (y
    /// generados, si lo eran) y suma omitidos en la misma sentencia.
    /// </summary>
    private Task<int> DegradarNoDisponiblesAsync(Guid loteId, short partNumber, Guid[] ids, CancellationToken ct)
    {
        var omitido = ConsolidadoExportItemStatus.Omitido;
        var incluido = ConsolidadoExportItemStatus.Incluido;
        var codigo = ConsolidadoLoteOmisiones.AdjuntoNoDisponible;
        var motivo = ConsolidadoErrorTextos.ParaLote(codigo);
        var generado = ConsolidadoExportDeliveryMode.Generado;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH degradado AS (
                UPDATE tramites.consolidado_export_batch_items
                   SET status = {omitido},
                       omission_code = {codigo},
                       omission_reason = {motivo},
                       updated_at = now()
                 WHERE batch_id = {loteId} AND part_number = {partNumber} AND id = ANY ({ids}) AND status = {incluido}
                RETURNING delivery_mode)
            UPDATE tramites.consolidado_export_batches
               SET included_count = included_count - (SELECT count(*) FROM degradado),
                   omitted_count = omitted_count + (SELECT count(*) FROM degradado),
                   generated_count = generated_count - (SELECT count(*) FROM degradado WHERE delivery_mode = {generado}),
                   updated_at = now()
             WHERE id = {loteId}
            """,
            ct);
    }

    private Task<int> DescartarAsync(Guid loteId, short partNumber, CancellationToken ct)
    {
        var descartada = ConsolidadoExportPartStatus.Descartada;
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batch_parts
               SET status = {descartada}, lease_until = NULL, updated_at = now()
             WHERE batch_id = {loteId} AND part_number = {partNumber} AND status IN ({PartePendiente}, {ParteEmpaquetando})
            """,
            ct);
    }

    /// <summary>Lock del lote (primero, siempre). Devuelve su estado, un marcador si está borrado, o <c>null</c> si no existe.</summary>
    private async Task<string?> BloquearLoteAsync(Guid loteId, CancellationToken ct)
    {
        var borrado = Borrado;
        var estados = await db.Database.SqlQuery<string>(
            $"""
            SELECT CASE WHEN deleted_at IS NULL THEN status ELSE {borrado} END AS "Value"
              FROM tramites.consolidado_export_batches
             WHERE id = {loteId}
               FOR UPDATE
            """)
            .ToListAsync(ct).ConfigureAwait(false);
        return estados.Count == 0 ? null : estados[0];
    }

    /// <summary>¿El lote admite cierres de parte? Solo <c>en_proceso</c> y <c>empaquetando</c>.</summary>
    private static bool EsVivo(string? estado) =>
        estado is ConsolidadoExportStatus.EnProceso or ConsolidadoExportStatus.Empaquetando;

    private Task<T> EnTransaccionAsync<T>(Func<CancellationToken, Task<T>> cuerpo, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(
            cuerpo,
            async (_, c, token) =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                var resultado = await c(token).ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
                return resultado;
            },
            verifySucceeded: null,
            ct);
    }
}
