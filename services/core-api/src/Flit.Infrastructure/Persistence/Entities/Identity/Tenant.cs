namespace Flit.Infrastructure.Persistence.Entities.Identity;

public sealed class Tenant
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string LegalName { get; set; } = string.Empty;

    public string TaxId { get; set; } = string.Empty;

    public string TenantType { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Cabeza de grupo de la que cuelga este cliente (HU #12318). <c>null</c> = sin padre.
    /// Un único padre por cliente; profundidad máxima 2 forzada por la base de datos
    /// (<c>tr_tenants_hierarchy</c>).
    /// </summary>
    public Guid? ParentTenantId { get; set; }

    /// <summary>
    /// <c>true</c> = el cliente es cabeza de grupo y puede tener hijos (HU #12318). Independiente
    /// de <see cref="TenantType"/>: un OT RENTING puede ser cabeza de grupo.
    /// </summary>
    public bool IsGroupParent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public long RowVersion { get; set; }
}
