using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Catálogo GLOBAL de roles por tipo de entidad (HU #10505 / ADR-0023): ya no filtra por
/// <c>tenant_id</c> (la columna fue eliminada de <c>security.roles</c>/<c>security.role_permissions</c>).
/// La unicidad de negocio es <c>(code, target_entity_type)</c>.
/// HU #13440/#13441: <c>tenant_id</c> NULL = rol global; con valor = rol propio de una compañía. Las consultas del
/// Admin de Compañía (<c>*ForTenant*</c>, <c>*VisibleToTenant*</c>) filtran por tenant — la RLS de la tabla es nominal.
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
            ProductCode = data.ProductCode,
            TenantId = data.TenantId,
            IsSystem = false,
            IsActive = true,
            RowVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Roles.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsRoleCodeViolation(ex))
        {
            // Red de la carrera entre CodeExists/CodeTaken y el INSERT, y del caso en que un SuperAdmin crea un rol
            // global con el code de un rol de tenant (CodeExistsAsync solo compara el mismo target_entity_type): el índice
            // uq_roles_tenant_code o el trigger tr_roles_tenant_code_not_global (DDL 134) revientan con 23505.
            db.Entry(entity).State = EntityState.Detached;
            throw new RoleCodeDuplicateException();
        }

        return entity.Id;
    }

    /// <summary>23505 sobre los índices únicos de code o el trigger de codes reservados (DDL 134).</summary>
    public static bool IsRoleCodeViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (pg.ConstraintName is "uq_roles_tenant_code" or "uq_roles_code_target_entity_type"
            || pg.TableName == "roles"
            || pg.MessageText.StartsWith("ROLE_CODE_DUPLICATE", StringComparison.Ordinal));

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
            permissions,
            role.ProductCode,
            role.TenantId);
    }

    public async Task<bool> HasActiveUsersAsync(Guid id, CancellationToken ct)
    {
        // HU #13441: una invitación pendiente con este rol cuenta como uso — al aceptarla crearía una asignación
        // a un rol borrado. El rol primario vive en user_invitations.role_id y el resto en invitation_roles.
        return await db.UserRoleAssignments.AnyAsync(x => x.RoleId == id && x.DeletedAt == null, ct)
            || await db.UserInvitations.AnyAsync(
                i => i.Status == "pending" && i.DeletedAt == null && i.RoleId == id, ct)
            || await db.InvitationRoles.AnyAsync(
                r => r.RoleId == id && r.DeletedAt == null
                    && db.UserInvitations.Any(i => i.Id == r.InvitationId && i.Status == "pending" && i.DeletedAt == null),
                ct);
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
                r.CreatedAt,
                r.ProductCode,
                r.TenantId)
        ).ToListAsync(ct);

        return rows;
    }

    public async Task<IReadOnlyList<RoleSummary>> ListVisibleToTenantAsync(Guid tenantId, string targetEntityType, CancellationToken ct)
    {
        return await (
            from r in db.Roles.AsNoTracking()
            where r.TargetEntityType == targetEntityType && r.DeletedAt == null
                && (r.TenantId == null || r.TenantId == tenantId)
            let permCount = db.RoleGrants.Count(rg => rg.RoleId == r.Id)
            orderby r.Name
            select new RoleSummary(
                r.Id, r.TargetEntityType, r.Code, r.Name, r.Description, r.IsSystem, r.IsActive,
                permCount, r.CreatedAt, r.ProductCode, r.TenantId)
        ).ToListAsync(ct);
    }

    public async Task<RoleDetail?> GetVisibleToTenantAsync(Guid tenantId, Guid roleId, CancellationToken ct)
    {
        var visible = await db.Roles.AsNoTracking()
            .AnyAsync(r => r.Id == roleId && r.DeletedAt == null && (r.TenantId == null || r.TenantId == tenantId), ct);
        return visible ? await GetByIdAsync(roleId, ct) : null;
    }

    public async Task<bool> CodeTakenForTenantAsync(Guid tenantId, string code, CancellationToken ct)
    {
        return await db.Roles.AnyAsync(
            r => r.DeletedAt == null && r.Code.ToLower() == code.ToLower() && (r.TenantId == null || r.TenantId == tenantId), ct);
    }

    public async Task UpdateDetailsAsync(Guid roleId, string name, string? description, CancellationToken ct)
    {
        await db.Roles
            .Where(r => r.Id == roleId && r.DeletedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Name, name)
                .SetProperty(r => r.Description, description)
                .SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    public async Task<IReadOnlyList<PermissionInfo>> GetPermissionInfosAsync(IReadOnlyList<Guid> permissionIds, CancellationToken ct)
    {
        return await (
            from p in db.RbacActions.AsNoTracking()
            join m in db.SecurityModules.AsNoTracking() on p.ModuleId equals m.Id
            where permissionIds.Contains(p.Id) && p.IsActive && p.DeletedAt == null && m.DeletedAt == null
            select new PermissionInfo(p.Id, p.Slug, p.Name, m.Code, m.ProductCode)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlySet<string>> GetEnabledProductCodesAsync(Guid tenantId, CancellationToken ct)
    {
        var enabled = await db.Set<TenantProductEntity>().AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Enabled)
            .Select(t => t.ProductCode)
            .ToListAsync(ct);
        // El hub (plataforma) no se enciende ni se apaga por empresa.
        return new HashSet<string>(enabled, StringComparer.Ordinal) { "plataforma" };
    }

    public async Task<IReadOnlyList<PermissionInfo>> ListGrantablePermissionsAsync(IReadOnlyCollection<string> productCodes, CancellationToken ct)
    {
        return await (
            from p in db.RbacActions.AsNoTracking()
            join m in db.SecurityModules.AsNoTracking() on p.ModuleId equals m.Id
            where p.IsActive && p.DeletedAt == null && m.DeletedAt == null && productCodes.Contains(m.ProductCode)
            orderby m.Code, p.Slug
            select new PermissionInfo(p.Id, p.Slug, p.Name, m.Code, m.ProductCode)
        ).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetPermissionProductCodesAsync(IReadOnlyList<Guid> permissionIds, CancellationToken ct)
    {
        return await (
            from p in db.RbacActions.AsNoTracking()
            join m in db.SecurityModules.AsNoTracking() on p.ModuleId equals m.Id
            where permissionIds.Contains(p.Id)
            select m.ProductCode
        ).Distinct().ToListAsync(ct);
    }

    public async Task SetPermissionsAsync(
        Guid roleId,
        IReadOnlyList<Guid> permissionIds,
        CancellationToken ct)
    {
        // HU #13441: borrar + insertar va en una transacción; un fallo en el insert (permiso inexistente, producto
        // distinto) no puede dejar el rol sin permisos. Si ya hay una transacción abierta se reutiliza.
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;

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

        if (transaction is not null)
            await transaction.CommitAsync(ct);
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
