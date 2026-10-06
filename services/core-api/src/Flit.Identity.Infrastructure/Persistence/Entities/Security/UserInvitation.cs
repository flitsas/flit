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

    /// <summary>
    /// HU #10625: última vez que se (re)envió el correo de invitación (creación o reenvío).
    /// Usado para calcular el cooldown anti-abuso de <c>InvitationOptions.ResendCooldown</c>
    /// entre reenvíos consecutivos.
    /// </summary>
    public DateTimeOffset? LastSentAt { get; set; }
}
