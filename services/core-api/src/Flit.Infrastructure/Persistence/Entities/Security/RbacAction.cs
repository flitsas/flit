using Flit.Infrastructure.Persistence.Entities.Common;

namespace Flit.Infrastructure.Persistence.Entities.Security;

public sealed class RbacAction : AuditableEntity
{
    public Guid ModuleId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Action { get; set; } = "CUSTOM";

    public string? Description { get; set; }

    public string HttpMethod { get; set; } = string.Empty;

    public string RoutePattern { get; set; } = string.Empty;

    public string Scope { get; set; } = "tenant";

    public bool IsActive { get; set; } = true;
}
