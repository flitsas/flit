using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Catálogo GLOBAL de roles por tipo de entidad (HU #10505 / ADR-0023): ya no filtra por
/// <c>tenant_id</c> (la columna fue eliminada de <c>security.roles</c>/<c>security.role_permissions</c>).
/// La unicidad de negocio es <c>(code, target_entity_type)</c>.
/// </summary>
public sealed class RoleRepository(FlitDbContext db) : IRoleRepository
{
    public async Task<bool> CodeExistsAsync(string targetEntityType, string code, CancellationToken ct)
    {
        return await db.Roles
            .AnyAsync(r => r.TargetEntityType == targetEntityType && r.Code == code && r.DeletedAt == null, ct);
    }

    public async Task<Guid> CreateAsync(CreateRoleData data, CancellationToken ct)
    {
        var entity = new Role
        {
            TargetEntityType = data.TargetEntityType,
            Code = data.Code,
            Name = data.Name,
            Description = data.Description,
            IsSystem = false,
            IsActive = true,
            RowVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Roles.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.Id;
    }

    public async Task<RoleDetail?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles
            .AsNoTracking()
            .Where(r => r.Id == id && r.DeletedAt == null)
            .FirstOrDefaultAsync(ct);

        if (role is null)
            return null;

        var permissions = await (
            from rg in db.RoleGrants.AsNoTracking()
            join p in db.RbacActions.AsNoTracking() on rg.PermissionId equals p.Id
            where rg.RoleId == id && p.DeletedAt == null
            select new PermissionSlug(p.Id, p.Slug, p.Name)
        ).ToListAsync(ct);

        return new RoleDetail(
            role.Id,
            role.TargetEntityType,
            role.Code,
            role.Name,
            role.Description,
            role.IsSystem,
            role.IsActive && role.DeletedAt == null,
            permissions);
    }

    public async Task<bool> HasActiveUsersAsync(Guid id, CancellationToken ct)
    {
        return await db.UserRoleAssignments
            .AnyAsync(x => x.RoleId == id && x.DeletedAt == null, ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct)
    {
        await db.Roles
            .Where(r => r.Id == id && r.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DeletedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task<IReadOnlyList<RoleSummary>> ListByTargetEntityTypeAsync(string targetEntityType, CancellationToken ct)
    {
        var rows = await (
            from r in db.Roles.AsNoTracking()
            where r.TargetEntityType == targetEntityType && r.DeletedAt == null
            let permCount = db.RoleGrants.Count(rg => rg.RoleId == r.Id)
            orderby r.Name
            select new RoleSummary(
                r.Id,
                r.TargetEntityType,
                r.Code,
                r.Name,
                r.Description,
                r.IsSystem,
                r.IsActive,
                permCount,
                r.CreatedAt)
        ).ToListAsync(ct);

        return rows;
    }

    public async Task SetPermissionsAsync(
        Guid roleId,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct)
    {
        // Hard delete de role_permissions existentes para este rol
        await db.RoleGrants
            .Where(rg => rg.RoleId == roleId)
            .ExecuteDeleteAsync(ct);

        // Insertar nuevos grants
        if (permissionIds.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var newGrants = permissionIds.Select(permId => new RoleGrant
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                PermissionId = permId,
                CreatedAt = now,
            }).ToList();

            db.RoleGrants.AddRange(newGrants);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetActiveAsync(Guid roleId, bool isActive, CancellationToken ct)
    {
        await db.Roles
            .Where(r => r.Id == roleId && r.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsActive, isActive),
                ct);
    }
}
