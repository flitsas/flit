using Flit.Infrastructure.Persistence.Entities.Common;

namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class UserInvitation : AuditableEntity
{
    public Guid TenantId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public Guid? RoleId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public Guid InvitedBy { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }
}
