namespace Flit.Infrastructure.Persistence.Entities.Security;

/// <summary>
/// Catálogo GLOBAL de roles por tipo de entidad (<c>COMPANY</c> | <c>TRANSIT_OFFICE</c>),
/// HU #10505. Ya NO es tenant-scoped: no hereda <see cref="Entities.Common.TenantAuditableEntity"/>
/// sino <see cref="Entities.Common.AuditableEntity"/>. La protección de escritura es RBAC
/// (SuperAdmin), no RLS — ver ADR-0023 (excepción a checklist A4/A20, mismo patrón que ADR-0019).
/// </summary>
public sealed class Role : Entities.Common.AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsSystem { get; set; }

    /// <summary>Tipo de entidad de negocio a la que aplica este rol (<c>COMPANY</c> | <c>TRANSIT_OFFICE</c>).</summary>
    public string TargetEntityType { get; set; } = "COMPANY";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Producto del rol (<c>platform.products.code</c>, HU #12964 / ADR-0063). Un usuario tiene un rol
    /// por producto en cada empresa (decisión D1): <c>AdminCompany</c> y <c>SuperAdmin</c> son de
    /// <c>plataforma</c>; <c>admin_tramites</c> y los roles operativos, de <c>tramites</c>.
    /// </summary>
    public string ProductCode { get; set; } = "tramites";
}
