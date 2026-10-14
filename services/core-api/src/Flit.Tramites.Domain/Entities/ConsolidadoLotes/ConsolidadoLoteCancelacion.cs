namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// HU #13385 (Feature #13307, diseño 09 §2.4 pasos 3–4, CF-09) — operación de dominio de la cancelación de un lote activo
/// ya bloqueado por su dueño:
/// <list type="bullet">
///   <item>Lote: <c>cancelado</c>; <c>finished_at</c>, <c>expires_at</c> y <c>purged_at</c> = instante de la cancelación;
///   <c>dek_wrapped = NULL</c> (borrado criptográfico); contadores tal cual. Sale del índice único parcial de lote activo,
///   así que libera el cupo.</item>
///   <item>Partes: el mismo descarte que la purga (<see cref="ConsolidadoLotePurga.DescartarPartes"/>): cerradas →
///   <c>purgada</c>, sin cerrar → <c>descartada</c>.</item>
///   <item>Ítems vivos: NO se tocan aquí; los pasa a <c>cancelado</c> el repositorio con un <c>UPDATE</c> masivo (hasta
///   20.000 filas, riesgo R-7) en la misma transacción.</item>
/// </list>
/// Los consolidados que el lote ya generó viven en el trámite y no se revierten (CF-09).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var rutas = ConsolidadoLoteCancelacion.EsCancelable(lote)
///     ? ConsolidadoLoteCancelacion.Aplicar(lote, partes, DateTimeOffset.UtcNow, usuarioId)
///     : [];
/// </code>
/// </remarks>
public static class ConsolidadoLoteCancelacion
{
    /// <summary>Activo (<c>en_cola</c>, <c>en_proceso</c>, <c>empaquetando</c>) y sin borrado lógico.</summary>
    public static bool EsCancelable(ConsolidadoExportBatch lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        return ConsolidadoExportStatus.EsActivo(lote.Status) && lote.DeletedAt is null;
    }

    /// <summary>Aplica la cancelación y devuelve el <c>storage_path</c> de las partes purgadas (borrado best-effort).</summary>
    /// <exception cref="InvalidOperationException">El lote no es cancelable (terminal o borrado).</exception>
    public static IReadOnlyList<string> Aplicar(
        ConsolidadoExportBatch lote, IEnumerable<ConsolidadoExportBatchPart> partes, DateTimeOffset ahora, Guid usuarioId)
    {
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(partes);
        if (!EsCancelable(lote))
            throw new InvalidOperationException(
                $"El lote {lote.Id} no se puede cancelar en estado '{lote.Status}' (solo activos).");

        var purgadas = ConsolidadoLotePurga.DescartarPartes(lote, partes, ahora, usuarioId);

        lote.Status = ConsolidadoExportStatus.Cancelado;
        lote.DekWrapped = null;
        lote.FinishedAt = ahora;
        lote.ExpiresAt = ahora;
        lote.PurgedAt = ahora;
        lote.UpdatedAt = ahora;
        lote.UpdatedBy = usuarioId;
        return purgadas;
    }
}
