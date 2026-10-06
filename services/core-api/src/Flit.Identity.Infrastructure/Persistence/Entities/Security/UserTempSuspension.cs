namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class UserTempSuspension : Entities.Common.TenantAuditableEntity
{
    public Guid UserId { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    /// <summary>
    /// Fin de la suspensión, o <c>null</c> para desactivación indefinida (HU #10619 AC1):
    /// el usuario queda bloqueado hasta que un administrador lo reactive explícitamente.
    /// </summary>
    public DateTimeOffset? EndsAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Identity.User User { get; set; } = null!;

    public Identity.Tenant Tenant { get; set; } = null!;
}
