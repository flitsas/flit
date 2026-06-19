namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Correo exento de la regla de propiedad vehicular — <c>admin.tenant_whitelist_users</c>
/// (HU #10154 DDL, gestionada por HU #10191). RLS por <c>app.current_tenant_id</c>.
/// Unicidad por <c>(tenant_id, email)</c>.
/// </summary>
public sealed class TenantWhitelistUser
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Correo normalizado (lowercase + trim). PII alta.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Motivo opcional de la excepción.</summary>
    public string? Reason { get; set; }

    /// <summary>Operador (FK <c>identity.users</c>) que dio de alta el correo.</summary>
    public Guid? AddedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
