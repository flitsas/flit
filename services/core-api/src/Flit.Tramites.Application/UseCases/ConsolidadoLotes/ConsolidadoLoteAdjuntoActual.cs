using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13378 AC4 (diseño §2.4 «Plan B del snapshot», C6) — adjunto ACTUAL del tipo del lote para un trámite, cuando el
/// <c>storage_path</c> capturado en el ítem ya no se puede abrir. Solo lee: nunca genera ni escribe.
/// </summary>
public interface IConsolidadoLoteAdjuntoActual
{
    /// <summary><c>storage_path</c> del adjunto actual del tipo, o <c>null</c> si el trámite no tiene ninguno.</summary>
    Task<string?> StoragePathActualAsync(
        Guid procedureInstanceId, Guid tenantId, string tipoDocumento, CancellationToken ct = default);
}

/// <summary>
/// Implementación con la MISMA precedencia que el entregador del lote (<see cref="ConsolidadoLoteEntregador"/>, regla
/// «existente tal cual»): consolidado del wizard ⇒ <see cref="ConsolidadoEntregaModos.Existente"/>; maestro ⇒ primero el
/// adjunto radicado ante Quipux y, si no, el maestro existente. No mira estado, vigencia ni <c>Source</c>. Con la
/// compañía del ítem explícita (el carril corre sin contexto HTTP).
/// </summary>
/// <remarks>Uso de ejemplo: <c>var ruta = await actual.StoragePathActualAsync(tramiteId, tenantId, "consolidado", ct);</c>.</remarks>
public sealed class ConsolidadoLoteAdjuntoActual(
    IProcedureInstanceRepository repo,
    IMaestroRadicadoLookup? maestroRadicado = null) : IConsolidadoLoteAdjuntoActual
{
    private readonly IMaestroRadicadoLookup _maestroRadicado = maestroRadicado ?? NullMaestroRadicadoLookup.Instance;

    public async Task<string?> StoragePathActualAsync(
        Guid procedureInstanceId, Guid tenantId, string tipoDocumento, CancellationToken ct = default)
    {
        if (!LoteTipoDocumento.EsValido(tipoDocumento))
            throw new ArgumentException($"Tipo de documento no admitido por el lote: '{tipoDocumento}'.", nameof(tipoDocumento));

        var instance = await repo.GetByIdWithAttachmentsAsync(procedureInstanceId, tenantId, ct).ConfigureAwait(false);
        if (instance is null || instance.DeletedAt is not null)
            return null;

        if (string.Equals(tipoDocumento, LoteTipoDocumento.ConsolidadoMaestro, StringComparison.Ordinal))
        {
            var radicadoId = await _maestroRadicado.AttachmentRadicadoAsync(tenantId, instance.Id, ct).ConfigureAwait(false);
            var radicado = radicadoId is { } rid ? instance.Attachments.FirstOrDefault(a => a.Id == rid) : null;
            if (radicado is not null)
                return radicado.StoragePath;
        }

        return ConsolidadoEntregaModos.Existente(instance, tipoDocumento)?.StoragePath;
    }
}
