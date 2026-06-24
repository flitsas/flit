namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class TenantModuleGrant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ModuleId { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public Guid? GrantedBy { get; set; }

    public SecurityModule Module { get; set; } = null!;
}
