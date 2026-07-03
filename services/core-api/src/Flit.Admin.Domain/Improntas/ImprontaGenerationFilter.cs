namespace Flit.Admin.Domain.Improntas;

/// <summary>
/// Filtros opcionales + paginación para el listado del historial de improntas
/// (HU #10468 / ADR-0022), análogo a <c>TransitOfficeTenantListFilter</c>. La tabla
/// <c>admin.impronta_generations</c> no tiene RLS (dispensa A10, ADR-0022): es una vista
/// global de auditoría para SuperAdmin, no se filtra por tenant.
/// </summary>
public sealed class ImprontaGenerationFilter
{
    /// <summary>Coincidencia parcial e insensible a mayúsculas sobre la placa. Opcional.</summary>
    public string? Placa { get; init; }

    /// <summary>Coincidencia parcial e insensible a mayúsculas sobre el radicado. Opcional.</summary>
    public string? Radicado { get; init; }

    /// <summary>Límite inferior (inclusive) del rango de fecha de creación. Opcional.</summary>
    public DateTimeOffset? CreatedFrom { get; init; }

    /// <summary>Límite superior (inclusive) del rango de fecha de creación. Opcional.</summary>
    public DateTimeOffset? CreatedTo { get; init; }

    /// <summary>Página solicitada (1-based, ya normalizada).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página (ya normalizado dentro de límites válidos).</summary>
    public required int PageSize { get; init; }
}
