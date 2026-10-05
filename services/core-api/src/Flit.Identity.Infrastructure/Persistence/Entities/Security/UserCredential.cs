namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class UserCredential
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    public bool MustChangePassword { get; set; }

    public DateTimeOffset? PasswordChangedAt { get; set; }

    public short FailedLoginAttempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public long RowVersion { get; set; }

    public Identity.User User { get; set; } = null!;
}
