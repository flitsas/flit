using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13385 (Feature #13307, diseño 09 §2.4 pasos 1–5) — cancelación del lote por su dueño en UNA transacción:
/// <list type="number">
///   <item><c>SELECT … FOR UPDATE</c> del lote filtrado por <c>requested_by_user_id = sub</c> (primer lock: orden lote →
///   ítem/parte, el mismo del reclamo #13376, los cierres #13375/#13376, el empaquetado #13378 y la purga).</item>
///   <item><c>UPDATE</c> masivo de los ítems vivos a <c>cancelado</c> (libera el índice parcial de reclamo; el cierre
///   en vuelo, condicionado a <c>procesando</c>, actualiza 0 filas).</item>
///   <item>Partes y lote con <see cref="ConsolidadoLoteCancelacion"/> (cerradas → <c>purgada</c>, sin cerrar →
///   <c>descartada</c>, DEK destruida, <c>cancelado</c> con <c>finished_at = expires_at = purged_at</c>).</item>
///   <item>Fila <c>lote_cancelado</c> con total, incluidos, omitidos y generados (<c>ck_…_audit_cancelled</c>) y
///   <c>parts_count</c> NULL (<c>ck_…_audit_parts</c>); <c>reached_tenant_ids</c> = compañías distintas de los ítems.</item>
/// </list>
/// Un solo <c>SaveChanges</c> + <c>COMMIT</c>: si la auditoría (o cualquier escritura) falla, todo se revierte.
/// </summary>
internal sealed partial class ConsolidadoLoteRepository
{
    public async Task<CancelarLoteResultado> CancelarAsync(CancelacionLote solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                solicitud,
                async (_, s, token) => await CancelarEnTransaccionAsync(s, token).ConfigureAwait(false),
                verifySucceeded: null,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            // Solo el tipo y el SQLSTATE: el mensaje del motor puede citar valores de la fila.
            LogNoCancelado(_logger, solicitud.LoteId, ex.GetType().Name, BuscarPostgres(ex)?.SqlState);
            return new CancelarLoteResultado(CancelarLoteEstado.NoRegistrado);
        }
    }

    private async Task<CancelarLoteResultado> CancelarEnTransaccionAsync(CancelacionLote s, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

        // Paso 1 — lock del lote del dueño. Otro usuario (incluido un Super Admin) no lo encuentra: 404, no 403.
        var lotes = await db.ConsolidadoExportBatches
            .FromSqlInterpolated($"""
                SELECT * FROM tramites.consolidado_export_batches
                 WHERE id = {s.LoteId} AND requested_by_user_id = {s.UsuarioId} AND deleted_at IS NULL
                   FOR UPDATE
                """)
            .ToListAsync(ct).ConfigureAwait(false);
        if (lotes.Count == 0)
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return new CancelarLoteResultado(CancelarLoteEstado.NoEncontrado);
        }

        var lote = lotes[0];
        if (lote.Status == ConsolidadoExportStatus.Cancelado || !ConsolidadoLoteCancelacion.EsCancelable(lote))
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            return new CancelarLoteResultado(
                lote.Status == ConsolidadoExportStatus.Cancelado ? CancelarLoteEstado.YaCancelado : CancelarLoteEstado.Terminado,
                lote);
        }

        var ahora = DateTimeOffset.UtcNow;

        // Paso 2 — ítems vivos (pendiente/procesando) a cancelado, en una sola sentencia (R-7: medido en la integración).
        var pendiente = ConsolidadoExportItemStatus.Pendiente;
        var procesando = ConsolidadoExportItemStatus.Procesando;
        var cancelado = ConsolidadoExportItemStatus.Cancelado;
        var itemsCancelados = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batch_items
               SET status = {cancelado}, lease_until = NULL, updated_at = {ahora}, updated_by = {s.UsuarioId}
             WHERE batch_id = {lote.Id} AND status IN ({pendiente}, {procesando})
            """,
            ct).ConfigureAwait(false);

        // Pasos 3–4 — partes y lote (misma regla de descarte que la purga).
        var partes = await db.ConsolidadoExportBatchParts
            .Where(p => p.BatchId == lote.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var rutas = ConsolidadoLoteCancelacion.Aplicar(lote, partes, ahora, s.UsuarioId);

        // Paso 5 — auditoría en la misma transacción. reached_tenant_ids: compañías de los ítems (AC9, D6).
        var companias = await db.ConsolidadoExportBatchItems.AsNoTracking()
            .Where(i => i.BatchId == lote.Id)
            .Select(i => i.TenantId)
            .Distinct()
            .OrderBy(t => t)
            .ToArrayAsync(ct).ConfigureAwait(false);
        db.ConsolidadoExportAuditEntries.Add(new ConsolidadoExportAuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = ahora,
            Event = ConsolidadoExportAuditEvent.LoteCancelado,
            Origin = lote.Origin,
            BatchId = lote.Id,
            ActorUserId = s.UsuarioId,
            // ck_consolidado_export_audit_tenant_origin: NULL si y solo si el origen es superadmin (el del lote).
            ActorTenantId = lote.TenantId,
            ActorRoleCode = s.RolCodigo,
            ScopeTenantId = lote.ScopeTenantId,
            ReachedTenantIds = companias,
            DocumentType = lote.DocumentType,
            SelectionMode = lote.SelectionMode,
            TotalItems = lote.TotalItems,
            IncludedCount = lote.IncludedCount,
            OmittedCount = lote.OmittedCount,
            GeneratedCount = lote.GeneratedCount,
            ClientIp = s.ClientIp,
            UserAgent = s.UserAgent,
        });

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new CancelarLoteResultado(CancelarLoteEstado.Cancelado, lote, itemsCancelados, rutas);
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote de consolidados {LoteId} no cancelado: {Tipo} {SqlState}. Transacción revertida; el lote sigue activo.")]
    private static partial void LogNoCancelado(ILogger logger, Guid loteId, string tipo, string? sqlState);
}
