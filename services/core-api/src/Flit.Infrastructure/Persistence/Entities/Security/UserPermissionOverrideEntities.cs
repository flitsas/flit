using Flit.Infrastructure.Persistence.Entities.Common;

namespace Flit.Infrastructure.Persistence.Entities.Security;

/// <summary><c>security.user_permission_overrides</c> (HU #13642, ADR-0071).</summary>
public sealed class UserPermissionOverrideEntity : TenantAuditableEntity
{
    public Guid UserId { get; set; }

    public string ProductCode { get; set; } = string.Empty;

    public Guid PermissionId { get; set; }

    public string Effect { get; set; } = string.Empty;

    public string Source { get; set; } = "INDIVIDUAL";

    public Guid? BatchId { get; set; }

    public Guid GrantedBy { get; set; }
}

/// <summary><c>security.user_permission_audit</c>: append-only, nunca se actualiza ni borra.</summary>
public sealed class UserPermissionAuditEntity
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ActorUserId { get; set; }

    public Guid TargetUserId { get; set; }

    public string? ProductCode { get; set; }

    public string? ModuleCode { get; set; }

    public string? PermissionSlug { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Outcome { get; set; } = "OK";

    public string? ReasonCode { get; set; }

    public string? ValueBefore { get; set; }

    public string? ValueAfter { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
