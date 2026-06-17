namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class PasswordResetToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }

    public Guid? RequestedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Identity.User User { get; set; } = null!;
}
