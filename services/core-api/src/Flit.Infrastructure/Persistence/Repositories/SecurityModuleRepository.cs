using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Modules;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class SecurityModuleRepository(FlitDbContext db) : ISecurityModuleRepository
{
    public async Task<bool> CodeExistsAsync(string code, Guid? excludeId, CancellationToken ct)
    {
        return await db.SecurityModules
            .AnyAsync(
                x => x.Code == code
                     && x.DeletedAt == null
                     && (excludeId == null || x.Id != excludeId.Value),
                ct);
    }

    public async Task<Guid> CreateAsync(SecurityModuleData data, CancellationToken ct)
    {
        var entity = new SecurityModule
        {
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            SortOrder = data.SortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.SecurityModules.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<SecurityModuleDetail?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await db.SecurityModules
            .AsNoTracking()
            .Where(x => x.Id == id && x.DeletedAt == null)
            .Select(x => new SecurityModuleDetail(
                x.Id, x.Code, x.Name, x.Description, x.SortOrder, x.IsActive, x.DeletedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdateAsync(Guid id, UpdateModuleData data, CancellationToken ct)
    {
        await db.SecurityModules
            .Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, data.Name)
                .SetProperty(x => x.Description, data.Description)
                .SetProperty(x => x.SortOrder, data.SortOrder)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        await db.SecurityModules
            .Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task ActivateAsync(Guid id, CancellationToken ct)
    {
        await db.SecurityModules
            .Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, true)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task<bool> HasActivePermissionsAsync(Guid id, CancellationToken ct)
    {
        return await db.RbacActions
            .AnyAsync(x => x.ModuleId == id && x.IsActive, ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        await db.SecurityModules
            .Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task<IReadOnlyList<SecurityModuleSummary>> ListAsync(CancellationToken ct)
    {
        var modules = await db.SecurityModules
            .AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.Description,
                x.SortOrder,
                x.IsActive,
                PermissionCount = db.RbacActions.Count(a => a.ModuleId == x.Id && a.IsActive),
            })
            .ToListAsync(ct);

        return modules
            .Select(x => new SecurityModuleSummary(
                x.Id, x.Code, x.Name, x.Description, x.SortOrder, x.IsActive, x.PermissionCount))
            .ToList();
    }

    public async Task<IReadOnlyList<AccessibleModuleDto>> ListAccessibleAsync(
        IReadOnlyList<string> permissionSlugs,
        bool includeAll,
        Guid? tenantId,
        string? targetEntityType,
        CancellationToken ct)
    {
        var query = from m in db.SecurityModules.AsNoTracking()
                    join a in db.RbacActions.AsNoTracking() on m.Id equals a.ModuleId
                    where m.IsActive && m.DeletedAt == null && a.IsActive && a.DeletedAt == null
                    select new { m.Id, m.Code, m.Name, m.SortOrder, ActionId = a.Id, ActionSlug = a.Slug, ActionName = a.Name };

        // NO tocar este branch (HU10163 — opt-in por tenant normal, ya en producción): si el
        // tenant llamante tiene algún TenantModuleGrant se restringe a sus grants; si no tiene
        // ninguno, ve todos los módulos según sus permisos.
        if (!includeAll)
        {
            query = query.Where(x => permissionSlugs.Contains(x.ActionSlug));

            if (tenantId.HasValue)
            {
                var tid = tenantId.Value;
                var hasGrants = await db.TenantModuleGrants.AnyAsync(g => g.TenantId == tid, ct);
                if (hasGrants)
                    query = query.Where(x => db.TenantModuleGrants.Any(g => g.TenantId == tid && g.ModuleId == x.Id));
            }
        }
        else if (targetEntityType is "COMPANY" or "TRANSIT_OFFICE")
        {
            // HU #10504 — constructor de roles SuperAdmin: solo se restringe cuando viene un tipo
            // de entidad destino explícito. Un módulo es visible si no tiene ningún scope definido
            // (global) o si tiene al menos un grant hacia un tenant del tipo pedido.
            var wantOt = targetEntityType == "TRANSIT_OFFICE";
            var visibleModuleIds = await db.SecurityModules
                .AsNoTracking()
                .Where(m => m.IsActive && m.DeletedAt == null)
                .Where(m =>
                    !db.TenantModuleGrants.Any(g => g.ModuleId == m.Id) ||
                    db.TenantModuleGrants.Any(g => g.ModuleId == m.Id &&
                        db.TransitOfficeProfiles.Any(p => p.TenantId == g.TenantId) == wantOt))
                .Select(m => m.Id)
                .ToListAsync(ct);

            query = query.Where(x => visibleModuleIds.Contains(x.Id));
        }
        // targetEntityType == null con includeAll=true: comportamiento sin cambios (todos los
        // módulos), lo sigue usando la pantalla "Módulos y Permisos" de SuperAdmin.

        var rows = await query
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ActionSlug)
            .ToListAsync(ct);

        return rows
            .GroupBy(x => new { x.Id, x.Code, x.Name, x.SortOrder })
            .Select(g => new AccessibleModuleDto(
                g.Key.Id,
                g.Key.Code,
                g.Key.Name,
                g.Key.SortOrder,
                g.Select(x => new AccessibleActionDto(x.ActionId, x.ActionSlug, x.ActionName)).ToList()))
            .ToList();
    }
}
