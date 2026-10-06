namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class UserRoleAssignment : Entities.Common.TenantAuditableEntity
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public Guid? AssignedBy { get; set; }

    public Identity.User User { get; set; } = null!;

    public Role Role { get; set; } = null!;
}
