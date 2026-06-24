using System.ComponentModel.DataAnnotations.Schema;
using Flit.Infrastructure.Persistence.Entities.Common;

namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class SecurityModule : AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public short SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    [NotMapped]
    public int PermissionCount { get; set; }
}
