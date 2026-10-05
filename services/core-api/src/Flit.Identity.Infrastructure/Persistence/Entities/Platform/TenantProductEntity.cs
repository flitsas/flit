namespace Flit.Infrastructure.Persistence.Entities.Platform;

/// <summary>
/// Producto encendido o apagado para una empresa — <c>platform.tenant_products</c> (HU #12958,
/// ADR-0063, contrato v1 §4). Una fila por <c>(tenant_id, product_code)</c>; sin fila = apagado.
/// Tenant-scoped: el aislamiento real es el <c>WHERE tenant_id</c> del repositorio (RLS sin FORCE).
/// </summary>
public sealed class TenantProductEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Usuario del último cambio; <c>null</c> si lo hizo la migración o el disparador de empresa nueva.</summary>
    public Guid? UpdatedBy { get; set; }

    public long RowVersion { get; set; }
}
