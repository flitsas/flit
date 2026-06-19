namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class SecurityModule
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public short SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
