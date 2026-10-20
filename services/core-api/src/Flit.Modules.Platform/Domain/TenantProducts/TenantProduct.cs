namespace Flit.Modules.Platform.Domain.TenantProducts;

/// <summary>
/// Habilitación de un producto para una empresa (<c>platform.tenant_products</c>). Encendido o
/// apagado, sin estados, fechas ni cobro (contrato de plataforma v1, §4). Sin fila equivale a
/// apagado: la regla es fail-closed.
/// </summary>
/// <param name="TenantId">Empresa (<c>identity.tenants.id</c>).</param>
/// <param name="ProductCode">Producto del catálogo.</param>
/// <param name="Enabled">Encendido o apagado.</param>
/// <param name="Notes">Motivo opcional que deja el SuperAdmin.</param>
/// <param name="UpdatedAt">Último cambio.</param>
/// <param name="UpdatedBy">Quién hizo el último cambio; <c>null</c> si lo hizo una migración.</param>
public sealed record TenantProduct(
    Guid TenantId,
    string ProductCode,
    bool Enabled,
    string? Notes,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy);
