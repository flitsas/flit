namespace Flit.Infrastructure.Persistence.Entities.Identity;

public sealed class User : Entities.Common.AuditableEntity
{
    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Status { get; set; } = "pending";

    public Guid? HomeTenantId { get; set; }
}
