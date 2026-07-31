using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Formato de FUR que aplica a la instancia según la clasificación del vehículo (HU #10924).</summary>
public sealed record FurTemplateFormatResult(string Format, string? VehicleClass);

/// <summary>
/// HU #10924 (Feature #10918) — resuelve, para el frontend, qué plantilla de FUR aplica a la instancia
/// (AUTOMOTOR/MAQUINARIA/REMOLQUES) a partir de su <c>vehicle_class</c>. El backend es la fuente de verdad
/// (D1): el frontend solo lo muestra. Reutiliza <see cref="IFurTemplateResolver"/> (mismo catálogo que la
/// generación real), así front y back nunca divergen.
/// </summary>
public sealed class GetFurTemplateFormatHandler(
    IProcedureInstanceRepository repo,
    IFurTemplateResolver templateResolver)
{
    public async Task<(FurTemplateFormatResult? Result, string? Error)> HandleAsync(
        Guid id, Guid tenantId, CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithFurGraphAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var vehicleClass = instance.FieldValues
            .FirstOrDefault(f => string.Equals(f.FieldKey, "vehicle_class", StringComparison.OrdinalIgnoreCase))?
            .ValueText;

        var format = await templateResolver.ResolveAsync(vehicleClass, ct);
        return (new FurTemplateFormatResult(format.ToString().ToUpperInvariant(), vehicleClass), null);
    }
}
