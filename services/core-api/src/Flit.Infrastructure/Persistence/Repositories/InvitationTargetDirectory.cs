using Flit.Modules.Security.Domain.Auth;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #14421 (Feature #14420, ADR-0077): tenant destino de una invitación y roles que ve. Solo lectura.
/// <list type="bullet">
/// <item>Tipo de entidad: el tenant es organismo de tránsito si tiene perfil en <c>admin.transit_office_profiles</c> (mismo
/// criterio que <c>POST /security/invitations</c> y el listado de usuarios); si no, compañía.</item>
/// <item>Roles: <c>security.roles</c> es un catálogo GLOBAL (ADR-0023); además hay roles propios del tenant
/// (<c>tenant_id</c> = destino). Activos y sin <c>deleted_at</c>. La app conecta como owner de la BD, así que el aislamiento
/// sale de este filtro explícito y no de la RLS.</item>
/// </list>
/// </summary>
internal sealed class InvitationTargetDirectory(FlitDbContext db) : IInvitationTargetDirectory
{
    public async Task<InvitationTargetInfo> GetTargetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var exists = await db.Tenants.AsNoTracking()
            .AnyAsync(t => t.Id == tenantId, cancellationToken).ConfigureAwait(false);
        if (!exists)
        {
            return new InvitationTargetInfo(false, InvitationTargetKind.Company);
        }

        var isTransitOffice = await db.TransitOfficeProfiles.AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId, cancellationToken).ConfigureAwait(false);

        return new InvitationTargetInfo(
            true,
            isTransitOffice ? InvitationTargetKind.TransitOffice : InvitationTargetKind.Company);
    }

    public async Task<IReadOnlyList<InvitationRoleInfo>> ListVisibleRolesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking()
            .Where(r => r.IsActive && r.DeletedAt == null && (r.TenantId == null || r.TenantId == tenantId))
            .OrderBy(r => r.Name)
            .Select(r => new InvitationRoleInfo(r.Id, r.Code, r.Name, r.TargetEntityType, r.TenantId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return roles;
    }
}
