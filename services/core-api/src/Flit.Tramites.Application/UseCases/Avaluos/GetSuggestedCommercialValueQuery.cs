using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// Sugiere el valor comercial del vehículo agregando en PARALELO todas las fuentes de avalúo
/// registradas (Feature #10707, ADR-0029). Tolera fallo parcial: si una fuente falla o no tiene
/// datos, la respuesta incluye las demás y marca su estado; nunca lanza. El valor sugerido toma
/// la primera fuente disponible según prioridad (Fasecolda principal). HU #13348: las fuentes las consulta
/// core-consultas (<see cref="IAvaluoSugeridor"/>); core-api no tiene proveedores.
/// </summary>
public sealed class GetSuggestedCommercialValueHandler(
    IProcedureInstanceRepository instanceRepo,
    IAvaluoSugeridor sugeridor)
{
    public async Task<(SuggestedCommercialValue? Result, string? Error)> HandleAsync(
        Guid instanceId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var instance = await instanceRepo.GetByIdWithDetailsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "instance_not_found");

        var fieldValues = instance.FieldValues
            .ToDictionary(f => f.FieldKey, f => f.ValueText, StringComparer.OrdinalIgnoreCase);
        return (await sugeridor.SugerirAsync(instance.Id, instance.TenantId, fieldValues, ct), null);
    }
}
