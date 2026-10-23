using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12797 (Épica #12760, code-review menor 1) — carrera entre una entrega/generación bajo demanda y
/// la regeneración anticipada: ambos cargan el mismo PDF, el worker lo sustituye primero y borra su
/// binario; el guardado del segundo falla con conflicto de concurrencia (la fila que quería retirar ya no
/// existe). Servir entonces el adjunto CAPTURADO antes entregaría un PDF ya borrado (404 al descargar).
///
/// <para>Aquí se relee el trámite (tracker limpio, el grafo en memoria está obsoleto) y se sirve el
/// consolidado vigente real, sin aviso de fallo: la regeneración no falló, la ganó otro camino.</para>
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>bitacora.GenerarConRespaldoAsync(…, ct, (ex, c) =&gt;
/// ConsolidadoVigenteTrasConflicto.ResolverAsync(repo, ex, id, tenantId, "consolidado", c))</c>.
/// </remarks>
public static class ConsolidadoVigenteTrasConflicto
{
    /// <summary>
    /// Resultado con el adjunto vigente si <paramref name="ex"/> es un conflicto de concurrencia y el
    /// trámite tiene un consolidado del <paramref name="tipoAdjunto"/>; <c>null</c> en cualquier otro caso
    /// (el llamador aplica su tratamiento de fallo de siempre).
    /// </summary>
    public static async Task<GenerarConsolidadoResult?> ResolverAsync(
        IProcedureInstanceRepository repo,
        Exception ex,
        Guid id,
        Guid tenantId,
        string tipoAdjunto,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(ex);
        if (!repo.IsConcurrencyConflict(ex))
            return null;

        // El tracker arrastra el intento fallido (fila nueva añadida, previas marcadas para borrar) y la
        // instancia con el row_version viejo: sin limpiarlo, la relectura devolvería ese mismo grafo.
        repo.ResetTracking();
        var fresca = await repo.GetByIdWithAttachmentsAsync(id, tenantId, ct).ConfigureAwait(false);
        var vigente = fresca is null ? null : ConsolidadoEntregaModos.Existente(fresca, tipoAdjunto);
        return vigente is null
            ? null
            : new GenerarConsolidadoResult(
                new ConsolidadoDocumentDto(vigente.Id, vigente.Tipo, vigente.Filename, vigente.Sha256),
                Regenerado: false);
    }
}
