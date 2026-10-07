using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13375 (Épica #13216) — implementación de <see cref="IConsolidadoLoteItemProceso"/> sobre PostgreSQL. Cada
/// operación es UNA sentencia parametrizada (CTE <c>UPDATE … RETURNING</c> + <c>UPDATE</c> del lote), atómica sin
/// transacción explícita y compatible con la estrategia de reintentos de Npgsql:
/// <list type="bullet">
///   <item>el ítem solo cambia si sigue en <c>procesando</c> (un lote cancelado o un cierre repetido no se
///   sobrescribe ni cuenta dos veces);</item>
///   <item>los contadores del lote suben en la misma sentencia, y solo si el ítem cambió.</item>
/// </list>
/// Los CHECK del DDL 134 (snapshot del incluido, código del omitido, ítem vivo sin resultado) los garantiza la base.
/// No escribe placa ni radicado: son el snapshot congelado al crear el lote.
/// </summary>
/// <remarks>Uso de ejemplo: <c>await new ConsolidadoLoteItemProceso(db).ReprogramarAsync(new(loteId, itemId, 1, ahora.AddSeconds(30)), ct);</c>.</remarks>
public sealed class ConsolidadoLoteItemProceso(FlitDbContext db) : IConsolidadoLoteItemProceso
{
    private const string Procesando = ConsolidadoExportItemStatus.Procesando;

    public async Task<bool> MarcarIncluidoAsync(LoteItemIncluido cierre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cierre);
        var estado = ConsolidadoExportItemStatus.Incluido;
        var adjunto = cierre.Adjunto;
        var procesadoEn = cierre.ProcesadoEn.ToUniversalTime();
        var generados = string.Equals(cierre.DeliveryMode, ConsolidadoExportDeliveryMode.Generado, StringComparison.Ordinal) ? 1 : 0;

        var filas = await db.Database.ExecuteSqlInterpolatedAsync(
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
                RETURNING batch_id)
            UPDATE tramites.consolidado_export_batches b
               SET included_count = b.included_count + 1,
                   generated_count = b.generated_count + {generados},
                   updated_at = now()
              FROM cerrado
             WHERE b.id = cerrado.batch_id
            """,
            ct).ConfigureAwait(false);
        return filas == 1;
    }

    public async Task<bool> MarcarOmitidoAsync(LoteItemOmitido cierre, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cierre);
        var estado = ConsolidadoExportItemStatus.Omitido;
        var procesadoEn = cierre.ProcesadoEn.ToUniversalTime();

        var filas = await db.Database.ExecuteSqlInterpolatedAsync(
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
                RETURNING batch_id)
            UPDATE tramites.consolidado_export_batches b
               SET omitted_count = b.omitted_count + 1,
                   updated_at = now()
              FROM cerrado
             WHERE b.id = cerrado.batch_id
            """,
            ct).ConfigureAwait(false);
        return filas == 1;
    }

    public async Task<bool> ReprogramarAsync(LoteItemReintento reintento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reintento);
        var estado = ConsolidadoExportItemStatus.Pendiente;
        var siguiente = reintento.SiguienteIntentoEn.ToUniversalTime();

        var filas = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE tramites.consolidado_export_batch_items
               SET status = {estado},
                   attempts = {reintento.Intentos},
                   next_attempt_at = {siguiente},
                   lease_until = NULL,
                   claimed_by = NULL,
                   updated_at = now()
             WHERE id = {reintento.ItemId} AND batch_id = {reintento.BatchId} AND status = {Procesando}
            """,
            ct).ConfigureAwait(false);
        return filas == 1;
    }
}
