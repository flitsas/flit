namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Filtros del listado agrupado por persona (HU #11270, CF-05 / ADR-0040).
/// Semántica de persona: documento, estado (de la más reciente), vigencia y fechas. El número de radicado
/// es la excepción: filtra las VALIDACIONES antes de agrupar (se muestra la ligada a ese trámite y el
/// contador solo cuenta las que coinciden). El resto de filtros de validación (modalidad, partyRole,
/// provider, score, motivoRechazo) no aplican — la UI los deshabilita.
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

    /// <summary>
    /// Número de radicado del trámite ligado, por coincidencia PARCIAL e insensible a mayúsculas (el gestor
    /// teclea un trozo, p. ej. los últimos dígitos). Una prevalidación standalone no tiene radicado: no coincide.
    /// </summary>
    public string? ReferenceNumber { get; init; }

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
        || Standalone is not null
        || !string.IsNullOrWhiteSpace(ReferenceNumber);
}
