namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class UserTempSuspension : Entities.Common.TenantAuditableEntity
{
    public Guid UserId { get; set; }

    public DateTimeOffset StartsAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public string Reason { get; set; } = string.Empty;

    public Identity.User User { get; set; } = null!;

    public Identity.Tenant Tenant { get; set; } = null!;
}
