namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Filtros del listado agrupado por persona (HU #11270, CF-05 / ADR-0040).
/// Solo semántica de persona: documento, estado (de la más reciente), vigencia y fechas.
/// Los filtros de validación (referenceNumber, modalidad, partyRole, provider, score, motivoRechazo)
/// no aplican — la UI los deshabilita.
/// </summary>
public sealed class BiometricPersonGroupFilter
{
    public string? Name { get; init; }
    public string? DocumentType { get; init; }
    public string? DocumentNumber { get; init; }
    /// <summary>Estado de la validación más reciente de la persona.</summary>
    public string? Status { get; init; }
    public DateTimeOffset? CreatedFrom { get; init; }
    public DateTimeOffset? CreatedTo { get; init; }
    public string? VigenciaEstado { get; init; }
    public DateTimeOffset? ExpiraDesde { get; init; }
    public DateTimeOffset? ExpiraHasta { get; init; }
    public int? VenceEnDias { get; init; }
    public bool? Standalone { get; init; }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(Name)
        || !string.IsNullOrWhiteSpace(DocumentType)
        || !string.IsNullOrWhiteSpace(DocumentNumber)
        || !string.IsNullOrWhiteSpace(Status)
        || CreatedFrom is not null
        || CreatedTo is not null
        || !string.IsNullOrWhiteSpace(VigenciaEstado)
        || ExpiraDesde is not null
        || ExpiraHasta is not null
        || VenceEnDias is not null
        || Standalone is not null;
}
