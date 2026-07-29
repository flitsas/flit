using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Gate de generación documental del <b>GESTOR</b> (HU #11051): con el trámite en estado final
/// (<c>aprobado</c> / <c>anulado</c>) la documentación del expediente es la definitiva —la que el
/// organismo de tránsito tuvo a la vista al aprobar— y el gestor no puede regenerarla.
///
/// <para><b>Por qué vive aquí y no dentro de los handlers de generación.</b> Esos handlers son
/// compartidos: el sistema los invoca legítimamente <i>después</i> de que el trámite pasó a estado
/// final —la aprobación del OT regenera FUR, mandato, trámite virtual y certificados para que reflejen
/// las firmas definitivas (HU #10996, <c>AdminOtEndpoints</c>), y lo mismo hacen la asignación de placa
/// (<c>AdminPlateRangesEndpoints</c>), el consumidor de identidad validada
/// (<c>IdentityValidationCompletedConsumer</c>) y las transiciones de estado
/// (<c>TransitionProcedureInstanceCommand</c>)—. Un guard dentro del handler rompería esos cinco
/// flujos. Por eso lo aplican SOLO los endpoints del gestor (<c>/api/v1/tramites/…</c>).</para>
///
/// Lectura ligera: usa <see cref="IProcedureInstanceRepository.GetByIdAsync"/> (sin grafos), porque
/// solo necesita el <c>status</c>.
/// </summary>
public sealed class GeneracionDocumentalGestorGuard(IProcedureInstanceRepository repo)
{
    /// <summary>
    /// Comprueba si el gestor puede generar documentación del trámite.
    /// </summary>
    /// <returns>
    /// <c>null</c> si puede generar; <see cref="TramiteEstadoErrores.NoEncontrado"/> si la instancia no
    /// existe en el tenant; <see cref="TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal"/> si el
    /// trámite está en estado final.
    /// </returns>
    public async Task<string?> CheckAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct).ConfigureAwait(false);
        if (instance is null)
            return TramiteEstadoErrores.NoEncontrado;

        return TramiteEstado.PermiteGeneracionDocumentalDelGestor(instance.Status)
            ? null
            : TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal;
    }
}
