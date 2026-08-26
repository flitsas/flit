namespace Flit.Admin.Domain.Companies;

/// <summary>
/// Filtros opcionales + paginación para el listado de compañías (HU #10189, RF02).
/// Todos los filtros son independientes y opcionales; se combinan con AND cuando
/// están informados. <see cref="Page"/> y <see cref="PageSize"/> ya vienen
/// normalizados por la capa de aplicación.
/// </summary>
public sealed class CompanyListFilter
{
    /// <summary>Coincidencia parcial sobre NIT (<c>tax_id</c>). Opcional.</summary>
    public string? Nit { get; init; }

    /// <summary>Coincidencia parcial e insensible a mayúsculas sobre Razón Social. Opcional.</summary>
    public string? RazonSocial { get; init; }

    /// <summary>Filtro exacto por estado activo. Opcional.</summary>
    public bool? EstadoActivo { get; init; }

    /// <summary>Límite inferior inclusivo de fecha de creación. Opcional.</summary>
    public DateOnly? FechaDesde { get; init; }

    /// <summary>Límite superior inclusivo (día completo) de fecha de creación. Opcional.</summary>
    public DateOnly? FechaHasta { get; init; }

    /// <summary>
    /// Si es <c>true</c>, excluye tenants con perfil de Organismo de Tránsito
    /// (<c>admin.transit_office_profiles</c>). Por defecto <c>true</c> en el handler.
    /// </summary>
    public bool ExcludeTransitOffices { get; init; }

    /// <summary>Página solicitada (1-based, ya normalizada).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página (ya normalizado dentro de límites válidos).</summary>
    public required int PageSize { get; init; }
}
