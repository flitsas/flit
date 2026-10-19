using Flit.Modules.Security.Domain.PermissionOverrides;

namespace Flit.Modules.Security.Application.PermissionOverrides;

/// <summary>
/// HU #13647 (FB-17, FB-18): consulta de la bitácora de permisos individuales según el ámbito. El Super Admin ve todo
/// (tenant opcional); los demás solo su tenant del JWT y pedir otro lanza <see cref="PermissionAuditScopeException"/>.
/// </summary>
public sealed class QueryPermissionAuditHandler(IPermissionAuditReader reader)
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    public Task<PermissionAuditPage> HandleAsync(
        Guid callerTenantId, bool isSuperAdmin, Guid? tenantId, Guid? userId, Guid? actorId,
        DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, CancellationToken ct)
    {
        if (!isSuperAdmin && tenantId is { } requested && requested != callerTenantId)
        {
            throw new PermissionAuditScopeException();
        }

        var scoped = isSuperAdmin ? tenantId : callerTenantId;
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var filter = new PermissionAuditFilter(scoped, userId, actorId, from, to, Math.Max(page ?? 1, 1), size);
        return reader.QueryAsync(filter, ct);
    }
}
