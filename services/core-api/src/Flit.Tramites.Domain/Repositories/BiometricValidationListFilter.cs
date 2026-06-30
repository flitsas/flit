namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Filtros opcionales para el listado transversal de validaciones biométricas del tenant (HU #10347).
/// Todos los filtros informados se combinan con AND. Null o vacío = sin filtro en esa dimensión.
/// </summary>
public sealed class BiometricValidationListFilter
{
    public string? ReferenceNumber { get; init; }
    public string? Modalidad { get; init; }
    public string? Nombre { get; init; }
    public string? Parte { get; init; }
    public string? TipoDoc { get; init; }
    public string? Documento { get; init; }
    public string? Estado { get; init; }
    public string? Provider { get; init; }
    public int? ScoreMin { get; init; }
    public int? ScoreMax { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public string? MotivoRechazo { get; init; }

    /// <summary>True si al menos un criterio de filtrado está informado.</summary>
    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(ReferenceNumber)
        || !string.IsNullOrWhiteSpace(Modalidad)
        || !string.IsNullOrWhiteSpace(Nombre)
        || !string.IsNullOrWhiteSpace(Parte)
        || !string.IsNullOrWhiteSpace(TipoDoc)
        || !string.IsNullOrWhiteSpace(Documento)
        || !string.IsNullOrWhiteSpace(Estado)
        || !string.IsNullOrWhiteSpace(Provider)
        || ScoreMin is not null
        || ScoreMax is not null
        || CreatedFrom is not null
        || CreatedTo is not null
        || !string.IsNullOrWhiteSpace(MotivoRechazo);
}
