namespace Flit.Tramites.Domain.Entities.ConsolidadoLotes;

/// <summary>
/// Épica #13216 (HU #13373, ADR-0070 D4/H10a) — operación de dominio de la purga de un lote: borrado criptográfico.
/// La usan la creación de un lote nuevo (retención de un solo lote por usuario, CF-12) y la purga a las 24 h (#13379).
/// <list type="bullet">
///   <item>Lote: <c>dek_wrapped = NULL</c>, <c>purged_at</c>, estado <c>expirado</c>.</item>
///   <item>Partes <c>cerrada</c> → <c>purgada</c>; partes sin cerrar (<c>pendiente</c>/<c>empaquetando</c>) →
///   <c>descartada</c>; <c>fallida</c>/<c>descartada</c>/<c>purgada</c> no cambian.</item>
///   <item>Ítems: no se tocan. La placa se conserva (decisión S2 = b).</item>
/// </list>
/// Un lote activo no se purga: primero termina o se cancela (las CHECK del DDL 133 lo impedirían igual).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// if (ConsolidadoLotePurga.EsPurgable(lote)) ConsolidadoLotePurga.Aplicar(lote, partes, DateTimeOffset.UtcNow);
/// </code>
/// </remarks>
public static class ConsolidadoLotePurga
{
    /// <summary>Terminal, no borrado y aún con la purga pendiente.</summary>
    public static bool EsPurgable(ConsolidadoExportBatch lote)
    {
        ArgumentNullException.ThrowIfNull(lote);
        return !ConsolidadoExportStatus.EsActivo(lote.Status)
            && lote.PurgedAt is null
            && lote.DeletedAt is null;
    }

    /// <exception cref="InvalidOperationException">El lote no es purgable (activo, borrado o ya purgado).</exception>
    public static void Aplicar(
        ConsolidadoExportBatch lote, IEnumerable<ConsolidadoExportBatchPart> partes, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(lote);
        ArgumentNullException.ThrowIfNull(partes);
        if (!EsPurgable(lote))
            throw new InvalidOperationException(
                $"El lote {lote.Id} no se puede purgar en estado '{lote.Status}' (solo terminales sin purgar).");

        foreach (var parte in partes)
        {
            if (parte.BatchId != lote.Id)
                throw new InvalidOperationException($"La parte {parte.Id} no pertenece al lote {lote.Id}.");

            if (parte.Status == ConsolidadoExportPartStatus.Cerrada)
            {
                parte.Status = ConsolidadoExportPartStatus.Purgada;
                parte.PurgedAt = ahora;
                parte.UpdatedAt = ahora;
            }
            else if (parte.Status is ConsolidadoExportPartStatus.Pendiente or ConsolidadoExportPartStatus.Empaquetando)
            {
                parte.Status = ConsolidadoExportPartStatus.Descartada;
                parte.LeaseUntil = null;
                parte.UpdatedAt = ahora;
            }
        }

        lote.DekWrapped = null;
        lote.PurgedAt = ahora;
        lote.FinishedAt ??= ahora;
        lote.ExpiresAt ??= ahora;
        lote.Status = ConsolidadoExportStatus.Expirado;
        lote.UpdatedAt = ahora;
    }
}
