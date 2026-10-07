namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13375 (Épica #13216) — persistencia del desenlace de UN ítem ya reclamado. Cada operación es una sola
/// sentencia atómica <b>condicionada a que el ítem siga en <c>procesando</c></b>: si el lote se canceló o el
/// ítem ya se cerró, no escribe nada y devuelve <c>false</c> (el cierre en vuelo no sobrescribe).
/// Los contadores del lote (<c>included_count</c>, <c>omitted_count</c>, <c>generated_count</c>) suben en la
/// misma sentencia que cierra el ítem.
/// </summary>
/// <remarks>
/// El reclamo (<c>FOR UPDATE SKIP LOCKED</c>), el lease y la asignación de partes NO son de este puerto (#13376).
/// Uso de ejemplo: <c>var aplicado = await proceso.MarcarIncluidoAsync(new(loteId, itemId, adjunto, "existente", ahora), ct);</c>.
/// </remarks>
public interface IConsolidadoLoteItemProceso
{
    /// <summary>
    /// <c>procesando → incluido</c> con el snapshot del adjunto y el modo de entrega; <c>included_count + 1</c>
    /// y, si el modo es <c>generado</c>, <c>generated_count + 1</c>.
    /// </summary>
    Task<bool> MarcarIncluidoAsync(LoteItemIncluido cierre, CancellationToken ct = default);

    /// <summary><c>procesando → omitido</c> con código y texto legible; <c>omitted_count + 1</c>.</summary>
    Task<bool> MarcarOmitidoAsync(LoteItemOmitido cierre, CancellationToken ct = default);

    /// <summary>
    /// <c>procesando → pendiente</c> por error técnico: guarda los intentos y <c>next_attempt_at</c> y libera el
    /// lease. No toca los contadores del lote.
    /// </summary>
    Task<bool> ReprogramarAsync(LoteItemReintento reintento, CancellationToken ct = default);
}

/// <summary>Cierre de un ítem incluido.</summary>
public sealed record LoteItemIncluido(
    Guid BatchId, Guid ItemId, LoteItemAdjunto Adjunto, string DeliveryMode, DateTimeOffset ProcesadoEn);

/// <summary>Cierre de un ítem omitido. <paramref name="Intentos"/> queda en <c>attempts</c>.</summary>
public sealed record LoteItemOmitido(
    Guid BatchId, Guid ItemId, string Codigo, string Motivo, short Intentos, DateTimeOffset ProcesadoEn);

/// <summary>Reintento de un ítem por error técnico.</summary>
public sealed record LoteItemReintento(Guid BatchId, Guid ItemId, short Intentos, DateTimeOffset SiguienteIntentoEn);
