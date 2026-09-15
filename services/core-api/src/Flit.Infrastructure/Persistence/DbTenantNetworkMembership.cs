using Flit.Modules.Security.Application.Auth.Network;
using Flit.Queries.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Persistence;

/// <summary>
/// Implementación de <see cref="ITenantNetworkMembership"/> (HU #12422, ADR-0060 D3) sobre las
/// MISMAS consultas estructurales de <see cref="Repositories.DbTenantScopeResolver"/>
/// (<c>identity.tenants.tenant_type</c> + <c>parent_tenant_id</c>, HU #12321/#12406) y
/// <c>admin.v_active_network_domains</c> (HU #12416/#12418) — nunca datos del cliente. Fail-closed:
/// cualquier excepción o inconsistencia estructural (cabeza sin tipo de cabeza válido) resuelve a
/// <see cref="NetworkMembership.None"/>, igual criterio que <c>DbTenantScopeResolver</c> y
/// <c>CachedTenantDomainResolver</c>.
/// </summary>
internal sealed partial class DbTenantNetworkMembership(
    FlitDbContext db,
    ILogger<DbTenantNetworkMembership> logger) : ITenantNetworkMembership
{
    private readonly FlitDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ILogger<DbTenantNetworkMembership> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<NetworkMembership> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            return NetworkMembership.None;

        try
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => new { t.IsGroupParent, t.TenantType, t.IsActive, t.ParentTenantId })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (tenant is null)
                return NetworkMembership.None;

            Guid? headTenantId = null;

            // El propio tenant es cabeza MARCA_BLANCA activa (HU #12406: is_group_parent vale
            // exactamente "tenant_type es de cabeza", forzado por ck_tenants_group_parent_by_type).
            if (tenant is { IsGroupParent: true, IsActive: true }
                && string.Equals(tenant.TenantType, GroupKindCodes.MarcaBlanca, StringComparison.Ordinal))
            {
                headTenantId = tenantId;
            }
            else if (tenant.ParentTenantId is { } parentId)
            {
                var parent = await _db.Tenants
                    .AsNoTracking()
                    .Where(t => t.Id == parentId)
                    .Select(t => new { t.IsGroupParent, t.TenantType, t.IsActive })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (parent is { IsGroupParent: true, IsActive: true }
                    && string.Equals(parent.TenantType, GroupKindCodes.MarcaBlanca, StringComparison.Ordinal))
                {
                    headTenantId = parentId;
                }
            }

            if (headTenantId is not { } head)
                return NetworkMembership.None;

            var activeHost = await _db.ActiveNetworkDomains
                .AsNoTracking()
                .Where(d => d.HeadTenantId == head)
                .Select(d => (string?)d.Host)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return new NetworkMembership(head, IsMarcaBlancaNetwork: true, activeHost);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ResolutionFailed(_logger, tenantId, ex);
            return NetworkMembership.None;
        }
    }

    [LoggerMessage(
        EventId = 12422,
        Level = LogLevel.Warning,
        Message = "No se pudo resolver la pertenencia de red del tenant {TenantId}; se aplica sin red (fail-closed).")]
    private static partial void ResolutionFailed(ILogger logger, Guid tenantId, Exception ex);
}
