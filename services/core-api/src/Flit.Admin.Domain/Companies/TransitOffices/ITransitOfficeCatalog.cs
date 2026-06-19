namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Catálogo estático en memoria de organismos de tránsito (HU #10192, RF13). La
/// búsqueda es case-insensitive e insensible a tildes sobre nombre y código (AC1).
/// </summary>
public interface ITransitOfficeCatalog
{
    /// <summary>Todas las entradas del catálogo.</summary>
    IReadOnlyList<TransitOfficeEntry> All { get; }

    /// <summary>
    /// Filtra por <paramref name="term"/> (coincidencia parcial en nombre o código,
    /// case/diacritic-insensitive). Término vacío o nulo devuelve todo el catálogo.
    /// </summary>
    IReadOnlyList<TransitOfficeEntry> Search(string? term);

    /// <summary>Indica si existe un organismo con el <paramref name="id"/> dado.</summary>
    bool Exists(Guid id);

    /// <summary>Devuelve el organismo con el <paramref name="id"/> dado, o <c>null</c>.</summary>
    TransitOfficeEntry? GetById(Guid id);
}
