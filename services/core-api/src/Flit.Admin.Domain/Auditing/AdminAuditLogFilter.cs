namespace Flit.Admin.Domain.Auditing;

/// <summary>
/// Filtros opcionales + paginación (ya normalizada) para la consulta global del rastro
/// de auditoría administrativo/seguridad (HU #10679), análogo a
/// <c>ImprontaGenerationFilter</c>. <c>UserId</c> matchea actor (<c>changed_by</c>) O
/// afectado (<c>target_entity_id</c>) — R2 del requerimiento.
/// </summary>
public sealed class AdminAuditLogFilter
{
    /// <summary>Usuario actor o afectado. Opcional.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Compañía gestora u organismo de tránsito. Opcional.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Tipo de tenant: <c>COMPANY</c> | <c>TRANSIT_OFFICE</c>. Opcional.</summary>
    public string? TenantType { get; init; }

    /// <summary>Categoría transversal: users | roles | permissions | authentication | security | config.</summary>
    public string? Module { get; init; }

    /// <summary>Verbo de la operación auditada. Opcional.</summary>
    public string? Operation { get; init; }

    /// <summary>Desenlace: success | failure. Opcional.</summary>
    public string? Result { get; init; }

    /// <summary>Límite inferior (inclusive) del rango de <c>changed_at</c>. Opcional.</summary>
    public DateTimeOffset? DateFrom { get; init; }

    /// <summary>Límite superior (inclusive) del rango de <c>changed_at</c>. Opcional.</summary>
    public DateTimeOffset? DateTo { get; init; }

    /// <summary>Página solicitada (1-based, ya normalizada).</summary>
    public required int Page { get; init; }

    /// <summary>Tamaño de página (ya normalizado dentro de límites válidos).</summary>
    public required int PageSize { get; init; }
}
