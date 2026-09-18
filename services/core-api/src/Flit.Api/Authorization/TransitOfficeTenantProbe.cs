using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #12711 — ¿la compañía es un organismo de tránsito? Un usuario pertenece al perfil OT cuando su
/// tenant tiene <c>TransitOfficeProfile</c>: el mismo criterio que usa Seguridad para resolver el
/// perfil (<c>ResolveProfile</c>) y el catálogo de roles (<c>TRANSIT_OFFICE</c> | <c>COMPANY</c>).
/// Se decide por el tenant y no por el nombre del rol, porque los roles se renombran desde RBAC.
/// </summary>
public interface ITransitOfficeTenantProbe
{
    Task<bool> IsTransitOfficeAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class TransitOfficeTenantProbe(FlitDbContext db) : ITransitOfficeTenantProbe
{
    public Task<bool> IsTransitOfficeAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        db.TransitOfficeProfiles
            .AsNoTracking()
            .AnyAsync(p => p.TenantId == tenantId, cancellationToken);
}
