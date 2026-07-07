namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class RoleGrant
{
    public Guid Id { get; set; }

    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public Role Role { get; set; } = null!;

    public RbacAction Action { get; set; } = null!;
}
