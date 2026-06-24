using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Permissions;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

public sealed class PermissionRepository(FlitDbContext db) : IPermissionRepository
{
    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct)
    {
        return await db.RbacActions
            .AnyAsync(x => x.Slug == slug && x.DeletedAt == null, ct);
    }

    public async Task<bool> ModuleExistsAndIsActiveAsync(Guid moduleId, CancellationToken ct)
    {
        return await db.SecurityModules
            .AnyAsync(x => x.Id == moduleId && x.IsActive && x.DeletedAt == null, ct);
    }

    public async Task<Guid> CreateAsync(CreatePermissionData data, CancellationToken ct)
    {
        var entity = new RbacAction
        {
            ModuleId = data.ModuleId,
            Slug = data.Slug,
            Name = data.Name,
            Action = data.Action,
            HttpMethod = data.HttpMethod,
            RoutePattern = data.RoutePattern,
            Description = data.Description,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.RbacActions.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<bool> IsInUseByRolesAsync(Guid id, CancellationToken ct)
    {
        return await db.RoleGrants
            .AnyAsync(x => x.PermissionId == id, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        await db.RbacActions
            .Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        await db.RbacActions
            .Where(x => x.Id == id && x.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.DeletedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct)
    {
        return await db.RbacActions
            .AnyAsync(x => x.Id == id && x.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<PermissionSummary>> ListByModuleAsync(Guid? moduleId, CancellationToken ct)
    {
        var query = from p in db.RbacActions.AsNoTracking()
                    join m in db.SecurityModules.AsNoTracking() on p.ModuleId equals m.Id
                    where p.DeletedAt == null
                    select new { p, m };

        if (moduleId.HasValue)
            query = query.Where(x => x.p.ModuleId == moduleId.Value);

        var rows = await query
            .OrderBy(x => x.p.Slug)
            .Select(x => new PermissionSummary(
                x.p.Id,
                x.p.Slug,
                x.p.Name,
                x.p.Action,
                x.p.ModuleId,
                x.m.Name,
                x.p.HttpMethod,
                x.p.RoutePattern,
                x.p.IsActive))
            .ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<Guid>> ResolveActiveSlugIdsAsync(IReadOnlyList<string> slugs, CancellationToken ct)
    {
        if (slugs.Count == 0)
            return [];

        return await db.RbacActions
            .AsNoTracking()
            .Where(x => slugs.Contains(x.Slug) && x.IsActive && x.DeletedAt == null)
            .Select(x => x.Id)
            .ToListAsync(ct);
    }
}
