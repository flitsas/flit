using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Security;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Mantenimiento del alcance de compañías cliente por usuario de un OT (Admin OT / Super Admin). La lista de
/// compañías elegibles sale de <see cref="OtTenantScope"/> + <see cref="OtVisibleCompanies"/> SIN restricción por
/// usuario: es la misma definición de «compañía visible del OT» que usan las consultas y las métricas.
/// Ausencia de fila = <c>todos</c>; guardar <c>todos</c> borra la fila. Cada cambio deja un registro en la auditoría
/// de permisos (<c>security.user_permission_audit</c>, acción <see cref="AuditAction"/>).
/// </summary>
internal sealed class OtUserClientScopeRepository(
    FlitDbContext db,
    IEffectiveTransitOfficeListResolver? effectiveOffices = null) : IOtUserClientScopeRepository
{
    /// <summary>Acción de la bitácora de permisos; <c>reason_code</c> = «antes&gt;después:n compañías».</summary>
    public const string AuditAction = "CLIENT_SCOPE_CHANGED";

    private readonly OtTenantScope _scope = new(db, effectiveOffices);

    public async Task<IReadOnlyList<OtClientCompanyItem>> ListClientCompaniesAsync(
        Guid otTenantId, Guid? transitOfficeIdOverride, CancellationToken cancellationToken = default)
    {
        var items = await _scope.ExecuteAsync<List<OtClientCompanyItem>>(
            otTenantId,
            transitOfficeIdOverride,
            async (office, scopeTenantIds) =>
            {
                var visible = await _scope
                    .ListVisibleClientTenantIdsAsync(office, scopeTenantIds, cancellationToken).ConfigureAwait(false);
                var tenants = await db.Tenants.AsNoTracking()
                    .Where(t => visible.Contains(t.Id))
                    .Select(t => new { t.Id, t.LegalName, t.TaxId })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                return tenants
                    .OrderBy(t => t.LegalName, StringComparer.OrdinalIgnoreCase)
                    .Select(t => new OtClientCompanyItem(t.Id, t.LegalName, string.IsNullOrWhiteSpace(t.TaxId) ? null : t.TaxId))
                    .ToList();
            },
            cancellationToken).ConfigureAwait(false);

        return items ?? [];
    }

    public async Task<Guid?> ResolveTransitOfficeIdOfUserAsync(
        Guid otTenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        var belongs = await db.Users.AsNoTracking()
            .AnyAsync(
                u => u.Id == userId && u.DeletedAt == null
                    && (u.HomeTenantId == otTenantId
                        || db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.TenantId == otTenantId && a.DeletedAt == null)),
                cancellationToken)
            .ConfigureAwait(false);

        return belongs
            ? await _scope.ResolveTransitOfficeIdAsync(otTenantId, cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<OtUserClientScopeView> GetAsync(
        Guid otTenantId, Guid userId, CancellationToken cancellationToken = default) =>
        await _scope.ReadCrossTenantAsync(
            async () =>
            {
                var scope = await db.OtUserClientScopes.AsNoTracking()
                    .Where(s => s.TenantId == otTenantId && s.UserId == userId)
                    .Select(s => new { s.Id, s.Mode })
                    .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

                if (scope is null || scope.Mode != OtUserClientScopeModes.Especificos)
                {
                    return new OtUserClientScopeView(OtClientScopeModes.Todos, []);
                }

                var clients = await (
                        from c in db.OtUserClientScopeClients.AsNoTracking()
                        join t in db.Tenants.AsNoTracking() on c.ClientTenantId equals t.Id
                        where c.ScopeId == scope.Id
                        select new { t.Id, t.LegalName, t.TaxId })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                return new OtUserClientScopeView(
                    OtClientScopeModes.Especificos,
                    [.. clients
                        .OrderBy(t => t.LegalName, StringComparer.OrdinalIgnoreCase)
                        .Select(t => new OtClientCompanyItem(t.Id, t.LegalName, string.IsNullOrWhiteSpace(t.TaxId) ? null : t.TaxId))]);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, OtUserClientScopeSummary>> GetSummariesAsync(
        Guid otTenantId, CancellationToken cancellationToken = default)
    {
        var rows = await db.OtUserClientScopes.AsNoTracking()
            .Where(s => s.TenantId == otTenantId && s.Mode == OtUserClientScopeModes.Especificos)
            .Select(s => new { s.UserId, Count = s.Clients.Count })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.UserId, r => new OtUserClientScopeSummary(OtClientScopeModes.Especificos, r.Count));
    }

    public async Task<IReadOnlyDictionary<Guid, OtUserClientScopeSummary>> GetInvitationSummariesAsync(
        Guid otTenantId, CancellationToken cancellationToken = default)
    {
        // security.user_invitations tiene RLS por tenant: se lee dentro del scope del OT, sin depender del contexto del endpoint.
        var rows = await TenantRlsScope.ExecuteAsync(
            db.Database,
            otTenantId,
            async () => await (
                from c in db.OtInvitationClientScopeClients.AsNoTracking()
                join i in db.UserInvitations.AsNoTracking() on c.InvitationId equals i.Id
                where i.TenantId == otTenantId
                group c by c.InvitationId into g
                select new { InvitationId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(r => r.InvitationId, r => new OtUserClientScopeSummary(OtClientScopeModes.Especificos, r.Count));
    }

    public Task SetAsync(
        Guid otTenantId,
        Guid transitOfficeId,
        Guid userId,
        string mode,
        IReadOnlyCollection<Guid> clientTenantIds,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            db.Database,
            otTenantId,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var scope = await db.OtUserClientScopes
                    .Include(s => s.Clients)
                    .FirstOrDefaultAsync(s => s.UserId == userId && s.TransitOfficeId == transitOfficeId, cancellationToken)
                    .ConfigureAwait(false);

                var before = scope?.Mode == OtUserClientScopeModes.Especificos
                    ? OtClientScopeModes.Especificos
                    : OtClientScopeModes.Todos;

                if (mode == OtClientScopeModes.Todos)
                {
                    // Ausencia de fila = todos: se borra la fila (y por cascada la lista).
                    if (scope is not null)
                    {
                        db.OtUserClientScopes.Remove(scope);
                    }
                }
                else
                {
                    if (scope is null)
                    {
                        scope = new OtUserClientScopeEntity
                        {
                            Id = Guid.CreateVersion7(),
                            TenantId = otTenantId,
                            UserId = userId,
                            TransitOfficeId = transitOfficeId,
                            CreatedAt = now,
                            CreatedBy = actorUserId,
                        };
                        db.OtUserClientScopes.Add(scope);
                    }

                    scope.Mode = OtUserClientScopeModes.Especificos;
                    scope.UpdatedAt = now;
                    scope.UpdatedBy = actorUserId;

                    var wanted = clientTenantIds.ToHashSet();
                    foreach (var stale in scope.Clients.Where(c => !wanted.Contains(c.ClientTenantId)).ToList())
                    {
                        db.OtUserClientScopeClients.Remove(stale);
                    }

                    var existing = scope.Clients.Select(c => c.ClientTenantId).ToHashSet();
                    foreach (var add in wanted.Where(w => !existing.Contains(w)))
                    {
                        scope.Clients.Add(new OtUserClientScopeClientEntity
                        {
                            ScopeId = scope.Id,
                            ClientTenantId = add,
                            CreatedAt = now,
                            CreatedBy = actorUserId,
                        });
                    }
                }

                // Rastro en la auditoría de permisos (HU #13647): sin PII, solo modos y cantidad.
                var after = mode == OtClientScopeModes.Especificos ? OtClientScopeModes.Especificos : OtClientScopeModes.Todos;
                db.UserPermissionAudits.Add(new UserPermissionAuditEntity
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = otTenantId,
                    ActorUserId = actorUserId,
                    TargetUserId = userId,
                    Action = AuditAction,
                    Outcome = "OK",
                    ReasonCode = $"{before}>{after}:{clientTenantIds.Count}",
                    CreatedAt = now,
                });

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    public async Task SetInvitationScopeAsync(
        Guid invitationId, IReadOnlyCollection<Guid> clientTenantIds, Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        if (clientTenantIds.Count == 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.OtInvitationClientScopeClients.AddRange(clientTenantIds.Distinct().Select(id => new OtInvitationClientScopeClientEntity
        {
            InvitationId = invitationId,
            ClientTenantId = id,
            CreatedAt = now,
            CreatedBy = actorUserId,
        }));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
