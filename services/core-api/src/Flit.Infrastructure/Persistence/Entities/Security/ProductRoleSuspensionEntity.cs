namespace Flit.Infrastructure.Persistence.Entities.Security;

/// <summary>
/// <c>security.product_role_suspensions</c> (ADR-0089, HU #14587): un usuario suspendido en UN producto. La asignación del rol
/// del producto se cierra y esta fila guarda el rol para listarlo como SUSPENDIDO y recrearlo al reactivar. No suspende la
/// cuenta ni toca los roles de otros productos. Una fila por (empresa, usuario, producto); reactivar la borra.
/// </summary>
public sealed class ProductRoleSuspensionEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    /// <summary>Rol del producto que tenía al suspenderse; es el que se recrea al reactivar.</summary>
    public Guid RoleId { get; set; }

    public DateTimeOffset SuspendedAt { get; set; }

    /// <summary>Usuario que suspendió (@pii:low).</summary>
    public Guid SuspendedBy { get; set; }
}
