namespace Flit.Modules.Security.Domain.Auth;

/// <summary>Tipo de entidad del tenant destino de una invitación (alinea con <c>security.roles.target_entity_type</c>).</summary>
public enum InvitationTargetKind
{
    /// <summary>Compañía (<c>COMPANY</c>).</summary>
    Company = 0,

    /// <summary>Organismo de tránsito (<c>TRANSIT_OFFICE</c>): el tenant tiene perfil en <c>admin.transit_office_profiles</c>.</summary>
    TransitOffice = 1,
}

/// <param name="Exists">El tenant destino existe. Si no, <paramref name="Kind"/> no es significativo.</param>
/// <param name="Kind">Tipo de entidad del tenant destino.</param>
public sealed record InvitationTargetInfo(bool Exists, InvitationTargetKind Kind);

/// <summary>Rol activo y visible para invitar en un tenant: del catálogo global (<c>TenantId == null</c>) o propio del tenant.</summary>
public sealed record InvitationRoleInfo(
    Guid Id,
    string Code,
    string Name,
    string TargetEntityType,
    Guid? TenantId);

/// <summary>
/// Puerto de lectura de la política de alcance de invitaciones (Feature #14420 / ADR-0077): qué es el tenant destino y
/// qué roles activos puede ver. NO decide permisos — eso es <c>IInvitationScopePolicy</c> (Application) — y no escribe.
/// <para>
/// La app conecta como owner de la BD y la RLS no aplica al owner, así que el aislamiento sale del filtro explícito
/// (<c>TenantId == null || TenantId == tenantId</c>), no de la RLS (ADR-0023: el catálogo de roles es global).
/// </para>
/// </summary>
public interface IInvitationTargetDirectory
{
    /// <summary>Existencia y tipo de entidad del tenant destino.</summary>
    Task<InvitationTargetInfo> GetTargetAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Roles activos y no eliminados, globales o propios del tenant, ordenados por nombre.</summary>
    Task<IReadOnlyList<InvitationRoleInfo>> ListVisibleRolesAsync(Guid tenantId, CancellationToken cancellationToken);
}
