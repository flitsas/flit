namespace Flit.Tramites.Domain.Tramites.Catalog;

/// <summary>Entrada del catálogo persistido de colores de vehículo.</summary>
public sealed record VehicleColorEntry(Guid Id, string Code, string Name);

/// <summary>Catálogo RUNT de colores (<c>catalogs.vehicle_colors</c>) con búsqueda acotada.</summary>
public interface IVehicleColorCatalog
{
    /// <summary>
    /// Busca colores activos por código o nombre. Sin término devuelve los primeros
    /// <paramref name="limit"/> ordenados por nombre (para el dropdown inicial).
    /// </summary>
    Task<IReadOnlyList<VehicleColorEntry>> SearchAsync(
        string? term,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Candidatos activos cuyo nombre tiene las MISMAS palabras que <paramref name="name"/> en el mismo orden,
    /// sin importar mayúsculas ni la cantidad de espacios entre ellas (sin límite de resultados). El llamador
    /// hace la comparación exacta final. HU #14602: <see cref="SearchAsync"/> (contiene + máximo 100 por nombre)
    /// no sirve para buscar uno exacto en un catálogo de ~20 000 colores: «GRIS» queda fuera del top 100.
    /// </summary>
    Task<IReadOnlyList<VehicleColorEntry>> FindByWordsAsync(
        string name,
        CancellationToken cancellationToken = default);
}
