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
}
