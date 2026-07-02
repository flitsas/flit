namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Filtros opcionales + paginación para el listado de tenants OT, análogo a
/// <c>CompanyListFilter</c>.
/// </summary>
public sealed class TransitOfficeTenantListFilter
{
    /// <summary>Coincidencia parcial sobre razón social. Opcional.</summary>
    public string? LegalName { get; init; }

    /// <summary>Filtro exacto por estado activo. Opcional.</summary>
    public bool? EstadoActivo { get; init; }

    /// <summary>Página solicitada (1-based, ya normalizada).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página (ya normalizado dentro de límites válidos).</summary>
    public required int PageSize { get; init; }
}
