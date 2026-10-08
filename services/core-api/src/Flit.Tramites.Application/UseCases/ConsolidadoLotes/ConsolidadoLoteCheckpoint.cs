using Flit.Tramites.Domain.Entities.ConsolidadoLotes;

namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13386 (Épica #13216, diseño 09 §2.4, CF-09) — regla única de los checkpoints cooperativos de los carriles (ítems y
/// empaquetado) sobre el estado leído con <c>IConsolidadoLoteRepository.GetStatusAsync</c>: el lote está
/// <b>detenido</b> si ya no existe (o tiene borrado lógico, <c>null</c>) o si está en un estado terminal
/// (<c>cancelado</c>, <c>fallido</c>, <c>completado*</c>, <c>expirado</c>). En ese caso el carril deja de trabajar en él.
/// La lectura es una foto sin lock: la decisión firme la toman los cierres condicionados bajo el lock del lote.
/// </summary>
/// <remarks>Uso de ejemplo: <c>if (ConsolidadoLoteCheckpoint.Detenido(await lotes.GetStatusAsync(id, ct))) return;</c>.</remarks>
public static class ConsolidadoLoteCheckpoint
{
    /// <summary><c>true</c> si el carril no debe seguir trabajando en el lote.</summary>
    public static bool Detenido(string? estado) =>
        estado is null || ConsolidadoExportStatus.Terminales.Contains(estado);
}
