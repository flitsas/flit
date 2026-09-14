namespace Flit.Infrastructure.Persistence.Entities.Identity;

/// <summary>
/// Entrada de la bitácora append-only del vínculo padre-hija — <c>identity.tenant_hierarchy_audit</c>
/// (HU #12323, ADR-0057). La escribe únicamente el trigger <c>tr_tenants_hierarchy_audit</c> al
/// cambiar <see cref="Tenant.ParentTenantId"/>; desde la aplicación es de solo lectura (UPDATE y
/// DELETE los rechaza la base). Sin FK a <c>identity.tenants</c> a propósito: la fila sobrevive al
/// desvínculo y a cualquier borrado futuro de padre o hijo.
/// </summary>
public sealed class TenantHierarchyAuditEntry
{
    public const string LinkAction = "LINK";

    public const string UnlinkAction = "UNLINK";

    public Guid Id { get; set; }

    public Guid ParentTenantId { get; set; }

    public Guid ChildTenantId { get; set; }

    /// <summary><see cref="LinkAction"/> o <see cref="UnlinkAction"/>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary><c>app.current_user_id</c> de la sesión o, en su defecto, <c>updated_by</c>/<c>created_by</c> del tenant.</summary>
    public Guid? ActorUserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
