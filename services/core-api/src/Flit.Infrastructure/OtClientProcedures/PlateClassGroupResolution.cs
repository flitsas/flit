using Flit.Admin.Domain.PlatePreassign;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.OtClientProcedures;

/// <summary>
/// HU8 [BACKEND] (Caso C 2026-11-09, D1) — resuelve el <see cref="PlateClassGroup"/> de un
/// <c>vehicle_class</c> crudo. Normaliza con <see cref="FurClassificationNormalizer"/> (Admin.Domain no ve esa
/// capa) y aplica <see cref="PlateRangeRules.ResolvePlateClassGroup"/>: listas exactas de carro, moto y
/// remolque; si ninguna aplica, el formato del catálogo <c>tramites.vehicle_classification_fur</c> vía
/// <see cref="IFurTemplateResolver"/> (cacheado en memoria por el resolver: una lectura del catálogo por
/// proceso, no por fila). MAQUINARIA ⇒ <see cref="PlateClassGroup.Maquinaria"/>, REMOLQUES ⇒
/// <see cref="PlateClassGroup.Remolque"/>; AUTOMOTOR (incluido el AUTOMOTOR por defecto sin match) ⇒ sin grupo.
/// Sin resolver (tests que construyen el repositorio a mano) solo cuentan las listas exactas.
/// </summary>
internal static class PlateClassGroupResolution
{
    public static async Task<PlateClassGroup?> ResolveAsync(
        string? rawVehicleClass, IFurTemplateResolver? furTemplateResolver, CancellationToken cancellationToken)
    {
        var normalized = FurClassificationNormalizer.Normalize(rawVehicleClass);
        if (normalized.Length == 0)
        {
            return null;
        }

        var exact = PlateRangeRules.ResolvePlateClassGroup(normalized, catalogGroup: null);
        if (exact is not null || furTemplateResolver is null)
        {
            return exact;
        }

        var match = await furTemplateResolver.ResolveMatchAsync(normalized, cancellationToken).ConfigureAwait(false);
        PlateClassGroup? catalogGroup = match.Format switch
        {
            FurTemplateFormat.Maquinaria => PlateClassGroup.Maquinaria,
            FurTemplateFormat.Remolques => PlateClassGroup.Remolque,
            _ => null,
        };
        return PlateRangeRules.ResolvePlateClassGroup(normalized, catalogGroup);
    }

    /// <summary>
    /// Resuelve varias clases a la vez (bandeja OT): una consulta al resolver por clase normalizada DISTINTA
    /// fuera de las listas exactas. Devuelve un diccionario clave = clase normalizada.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, PlateClassGroup?>> ResolveManyAsync(
        IEnumerable<string?> rawVehicleClasses, IFurTemplateResolver? furTemplateResolver, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, PlateClassGroup?>(StringComparer.Ordinal);
        foreach (var raw in rawVehicleClasses)
        {
            var normalized = FurClassificationNormalizer.Normalize(raw);
            if (normalized.Length == 0 || result.ContainsKey(normalized))
            {
                continue;
            }

            result[normalized] = await ResolveAsync(normalized, furTemplateResolver, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Busca el grupo de una clase cruda en el diccionario de <see cref="ResolveManyAsync"/>.</summary>
    public static PlateClassGroup? Lookup(IReadOnlyDictionary<string, PlateClassGroup?> groups, string? rawVehicleClass)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var normalized = FurClassificationNormalizer.Normalize(rawVehicleClass);
        return normalized.Length > 0 && groups.TryGetValue(normalized, out var group) ? group : null;
    }
}
