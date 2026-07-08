using Flit.Infrastructure.Persistence;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserRoles;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class AuthUserRepository(FlitDbContext db, IUserRoleAssignmentRepository userRoleAssignmentRepository) : IAuthUserRepository
{
    public async Task<UserAuthSnapshot?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null && EF.Functions.ILike(u.Email, normalizedEmail))
            .Select(u => new { u.Id, u.Email, u.Status, u.HomeTenantId })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return null;

        var credential = await db.UserCredentials
            .AsNoTracking()
            .Where(c => c.UserId == user.Id)
            .Select(c => new { c.PasswordHash, c.MustChangePassword })
            .FirstOrDefaultAsync(cancellationToken);

        if (credential is null)
            return null;

        // HU #10506: soporte multi-rol — se traen TODAS las asignaciones ACTIVAS del usuario
        // (no solo la primera), filtrando además que el rol siga activo en el catálogo global
        // (HU #10505 / ADR-0023: un rol puede desactivarse sin borrar la asignación).
        var assignments = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserId == user.Id && a.DeletedAt == null && r.DeletedAt == null && r.IsActive
            select new { a.TenantId, a.RoleId, RoleCode = r.Code }
        ).ToListAsync(cancellationToken);

        // HU #10507: conteo TOTAL de asignaciones que el usuario tuvo alguna vez activas
        // (UserRoleAssignment.DeletedAt == null), SIN filtrar por si el Role referenciado sigue
        // activo. La diferencia con assignments.Count distingue "nunca tuvo rol" (AC3) de
        // "tuvo roles, pero todos fueron desactivados" (AC2).
        var totalAssignedRolesCount = await db.UserRoleAssignments
            .AsNoTracking()
            .CountAsync(a => a.UserId == user.Id && a.DeletedAt == null, cancellationToken);

        // Users without an active role assignment can still log in if they have a home tenant
        // (HU #10507 bloquea el caso de "todos los roles inactivos" en el LoginHandler; aquí solo
        // se resuelve el tenant para poder construir el snapshot).
        var tenantId = assignments.Count > 0 ? assignments[0].TenantId : user.HomeTenantId;
        if (tenantId is null)
            return null;

        var tenant = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId.Value)
            .Select(t => new { t.LegalName, t.TaxId })
            .FirstOrDefaultAsync(cancellationToken);
        var tenantName = tenant?.LegalName ?? string.Empty;
        // HU #10616 AC4 — el NIT puede no estar registrado (columna requerida pero admite vacío
        // heredado); se emite tal cual, sin romper el login ni la emisión del JWT.
        var tenantTaxId = tenant?.TaxId ?? string.Empty;

        // HU #10616 AC1/AC2 — tipo de entidad del tenant (COMPANY | TRANSIT_OFFICE), mismo
        // criterio ya usado por SecurityEndpoints/UserRoleAssignmentRepository (HU #10504/#10506).
        var entityType = await userRoleAssignmentRepository.GetTenantTargetEntityTypeAsync(tenantId.Value, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var isSuspended = await db.UserTempSuspensions
            .AsNoTracking()
            .AnyAsync(
                s => s.UserId == user.Id
                     && s.TenantId == tenantId
                     && s.DeletedAt == null
                     && s.StartsAt <= now
                     && s.EndsAt >= now,
                cancellationToken);

        // permissionSlugs = UNIÓN distinct de permisos de TODOS los roles activos (antes solo
        // del primero) — HU #10506: multi-rol implica que los permisos efectivos son la unión.
        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var permissionSlugs = roleIds.Count == 0
            ? []
            : await (
                from rp in db.RoleGrants.AsNoTracking()
                join p in db.RbacActions.AsNoTracking() on rp.PermissionId equals p.Id
                where roleIds.Contains(rp.RoleId) && p.IsActive
                select p.Slug
            ).Distinct().ToListAsync(cancellationToken);

        var activeRoles = assignments
            .Select(a => new UserRoleSnapshot(a.RoleId, a.RoleCode))
            .DistinctBy(r => r.Id)
            .ToList();

        return new UserAuthSnapshot
        {
            UserId = user.Id,
            Email = user.Email,
            Status = user.Status,
            PasswordHash = credential.PasswordHash,
            MustChangePassword = credential.MustChangePassword,
            TenantId = tenantId.Value,
            TenantName = tenantName,
            TenantTaxId = tenantTaxId,
            EntityType = entityType,
            ActiveRoles = activeRoles,
            TotalAssignedRolesCount = totalAssignedRolesCount,
            PermissionSlugs = permissionSlugs,
            IsTemporarilySuspended = isSuspended,
        };
    }
}
