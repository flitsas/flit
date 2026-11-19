namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Alcance de compañías cliente de un usuario de un OT — <c>admin.ot_user_client_scopes</c>.
/// Ausencia de fila equivale a <see cref="OtUserClientScopeModes.Todos"/>. Sin RLS (lectura cross-tenant).
/// </summary>
public sealed class OtUserClientScopeEntity
{
    public Guid Id { get; set; }

    /// <summary>Tenant del OT dueño del usuario.</summary>
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    public Guid TransitOfficeId { get; set; }

    /// <summary><see cref="OtUserClientScopeModes"/>: <c>todos</c> | <c>especificos</c>.</summary>
    public string Mode { get; set; } = OtUserClientScopeModes.Todos;

    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public long RowVersion { get; set; }

    public List<OtUserClientScopeClientEntity> Clients { get; set; } = [];
}

/// <summary>Valores permitidos de <see cref="OtUserClientScopeEntity.Mode"/>.</summary>
public static class OtUserClientScopeModes
{
    public const string Todos = "todos";
    public const string Especificos = "especificos";
}

/// <summary>Compañía cliente asignada a un alcance 'especificos' — <c>admin.ot_user_client_scope_clients</c>.</summary>
public sealed class OtUserClientScopeClientEntity
{
    public Guid ScopeId { get; set; }

    public Guid ClientTenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public OtUserClientScopeEntity? Scope { get; set; }
}

/// <summary>
/// Compañía cliente elegida al invitar a un usuario del OT con alcance 'especificos' — <c>admin.ot_invitation_client_scope_clients</c>.
/// Se materializa en <see cref="OtUserClientScopeEntity"/> al aceptarse la invitación. Ausencia de filas = todos.
/// </summary>
public sealed class OtInvitationClientScopeClientEntity
{
    public Guid InvitationId { get; set; }

    public Guid ClientTenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
