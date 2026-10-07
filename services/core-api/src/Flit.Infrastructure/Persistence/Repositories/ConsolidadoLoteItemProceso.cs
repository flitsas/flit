using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13375 (Épica #13216) — implementación de <see cref="IConsolidadoLoteItemProceso"/> sobre PostgreSQL. Cada
/// operación es UNA sentencia parametrizada (CTE <c>UPDATE … RETURNING</c> + <c>UPDATE</c> del lote):
/// <list type="bullet">
///   <item>el ítem solo cambia si sigue en <c>procesando</c> (un lote cancelado o un cierre repetido no se
///   sobrescribe ni cuenta dos veces) <b>y con su reserva vigente</b> (<c>lease_until &gt; now()</c>, HU #13376 AC6:
///   un lease vencido ya es reclamable por otro slot y su cierre tardío no pisa el estado);</item>
///   <item>los contadores del lote suben en la misma sentencia, y solo si el ítem cambió.</item>
///   <item>HU #13376 — la sentencia corre en una transacción corta que antes toma el lock del lote
///   (<c>SELECT … FOR UPDATE</c>): orden lote → ítem, el mismo del reclamo y de la cancelación (#13307), así un
///   cierre y una cancelación simultáneos se serializan sin interbloqueo. La generación del PDF queda fuera.</item>
///   <item>HU #13377 — el cierre de un incluido asigna, en esa misma transacción, las partes ya llenas por N o M
///   (<see cref="ConsolidadoLotePartesAsignacion"/>, modo parcial).</item>
/// </list>
/// Los CHECK del DDL 134 (snapshot del incluido, código del omitido, ítem vivo sin resultado) los garantiza la base.
/// No escribe placa ni radicado: son el snapshot congelado al crear el lote.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new ConsolidadoLoteItemProceso(db).ReprogramarAsync(new(loteId, itemId, 1, ahora.AddSeconds(30)), ct);</c>.</remarks>
public sealed class ConsolidadoLoteItemProceso(FlitDbContext db) : IConsolidadoLoteItemProceso
{
    private const string Procesando = ConsolidadoExportItemStatus.Procesando;

    /// <summary>
    /// HU #13376 — ejecuta el cierre con el lock corto del lote. Dentro de la estrategia de reintentos del contexto
    /// (la transacción manual lo exige); si ya hay una transacción en curso, se une a ella.
    /// </summary>
    private async Task<bool> CerrarConLockDelLoteAsync(
        Guid batchId, Func<CancellationToken, Task<int>> sentencia, CancellationToken ct, bool asignarPartes = false)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await BloquearLoteAsync(batchId, ct).ConfigureAwait(false);
            return await CerrarYAsignarAsync(batchId, sentencia, asignarPartes, ct).ConfigureAwait(false);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (batchId, sentencia, asignarPartes),
            async (_, estado, token) =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);
                await BloquearLoteAsync(estado.batchId, token).ConfigureAwait(false);
                var aplicado = await CerrarYAsignarAsync(estado.batchId, estado.sentencia, estado.asignarPartes, token)
                    .ConfigureAwait(false);
                await tx.CommitAsync(token).ConfigureAwait(false);
                return aplicado;
            },
            verifySucceeded: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #13377 — tras cerrar un ítem <c>incluido</c>, en la misma transacción y bajo el mismo lock del lote, saca las
    /// partes que ya están llenas por N o M (<see cref="ModoAsignacion.Parcial"/>) para que el empaquetado (#13378) no
    /// espere al final del lote. Un omitido no llena partes: espera a la siguiente parte o al cierre del carril.
    /// </summary>
    private async Task<bool> CerrarYAsignarAsync(
        Guid batchId, Func<CancellationToken, Task<int>> sentencia, bool asignarPartes, CancellationToken ct)
    {
        if (await sentencia(ct).ConfigureAwait(false) != 1)
            return false;
        if (asignarPartes)
            await ConsolidadoLotePartesAsignacion.AsignarAsync(db, batchId, ModoAsignacion.Parcial, ct).ConfigureAwait(false);
        return true;
    }

    private Task<int> BloquearLoteAsync(Guid batchId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM tramites.consolidado_export_batches WHERE id = {batchId} FOR UPDATE",
            ct);

    public async Task<bool> MarcarIncluidoAsync(LoteItemIncluido cierre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cierre);
        var estado = ConsolidadoExportItemStatus.Incluido;
        var adjunto = cierre.Adjunto;
        var procesadoEn = cierre.ProcesadoEn.ToUniversalTime();
        var generados = string.Equals(cierre.DeliveryMode, ConsolidadoExportDeliveryMode.Generado, StringComparison.Ordinal) ? 1 : 0;

        return await CerrarConLockDelLoteAsync(
            cierre.BatchId,
            token => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH cerrado AS (
                UPDATE tramites.consolidado_export_batch_items
                   SET status = {estado},
                       attachment_id = {adjunto.AttachmentId},
                       storage_path = {adjunto.StoragePath},
                       size_bytes = {adjunto.SizeBytes},
                       delivery_mode = {cierre.DeliveryMode},
                       processed_at = {procesadoEn},
                       lease_until = NULL,
                       updated_at = now()
                 WHERE id = {cierre.ItemId} AND batch_id = {cierre.BatchId} AND status = {Procesando}
                   AND lease_until > now()
                RETURNING batch_id)
            UPDATE tramites.consolidado_export_batches b
               SET included_count = b.included_count + 1,
                   generated_count = b.generated_count + {generados},
                   updated_at = now()
              FROM cerrado
             WHERE b.id = cerrado.batch_id
            """,
            token),
            ct,
            asignarPartes: true).ConfigureAwait(false);
    }

    public async Task<bool> MarcarOmitidoAsync(LoteItemOmitido cierre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cierre);
        var estado = ConsolidadoExportItemStatus.Omitido;
        var procesadoEn = cierre.ProcesadoEn.ToUniversalTime();

        return await CerrarConLockDelLoteAsync(
            cierre.BatchId,
            token => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH cerrado AS (
                UPDATE tramites.consolidado_export_batch_items
                   SET status = {estado},
                       omission_code = {cierre.Codigo},
                       omission_reason = {cierre.Motivo},
                       attempts = {cierre.Intentos},
                       processed_at = {procesadoEn},
                       lease_until = NULL,
                       updated_at = now()
                 WHERE id = {cierre.ItemId} AND batch_id = {cierre.BatchId} AND status = {Procesando}
                   AND lease_until > now()
                RETURNING batch_id)
            UPDATE tramites.consolidado_export_batches b
               SET omitted_count = b.omitted_count + 1,
                   updated_at = now()
              FROM cerrado
             WHERE b.id = cerrado.batch_id
            """,
            token),
            ct).ConfigureAwait(false);
    }

    public async Task<bool> ReprogramarAsync(LoteItemReintento reintento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reintento);
        var estado = ConsolidadoExportItemStatus.Pendiente;
        var siguiente = reintento.SiguienteIntentoEn.ToUniversalTime();

        return await CerrarConLockDelLoteAsync(
            reintento.BatchId,
            token => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batch_items
               SET status = {estado},
                   attempts = {reintento.Intentos},
                   next_attempt_at = {siguiente},
                   lease_until = NULL,
                   claimed_by = NULL,
                   updated_at = now()
             WHERE id = {reintento.ItemId} AND batch_id = {reintento.BatchId} AND status = {Procesando}
               AND lease_until > now()
            """,
            token),
            ct).ConfigureAwait(false);
    }
}
