using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Security.Domain.UserRoles.ProductRoles;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// ADR-0089 (HU #14587) — operaciones por producto sobre <c>security.user_role_assignments</c> y
/// <c>security.product_role_suspensions</c>. Todas las escrituras se acotan a las asignaciones cuyo rol es del producto
/// (<c>roles.product_code</c>): las de otros productos del usuario no se tocan. <c>user_role_assignments.product_code</c> la
/// copia el disparador <c>tr_ura_product_code</c> (DDL 120). El espejo AdminCompany ⇒ admin de producto (DDL 126) solo
/// reacciona a filas de AdminCompany, así que asignar o cerrar un admin de producto aquí no lo dispara.
/// Solo LINQ parametrizado; sin logs (correo y nombre son PII).
/// </summary>
public sealed class ProductRoleRepository(FlitDbContext db) : IProductRoleRepository
{
    private const string SuperAdminRole = "SuperAdmin";
    private const string AdminCompanyRole = "AdminCompany";
    private const string Pending = "pending";
    private const string Active = "active";

    public async Task<ProductRoleRef?> FindAssignableRoleAsync(string productCode, string roleCode, CancellationToken ct) =>
        await db.Roles.AsNoTracking()
            .Where(r => r.Code == roleCode && r.ProductCode == productCode && r.TenantId == null
                && r.IsActive && r.DeletedAt == null && r.TargetEntityType == "COMPANY")
            .OrderBy(r => r.CreatedAt)
            .Select(r => new ProductRoleRef(r.Id, r.Code))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    public async Task<ProductMemberSnapshot?> GetMemberAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct)
    {
        var members = await SnapshotsAsync(tenantId, [userId], productCode, ct).ConfigureAwait(false);
        return members.Count == 1 ? members[0] : null;
    }

    public async Task<IReadOnlyList<ProductMemberSnapshot>> ListMembersAsync(Guid tenantId, string productCode, CancellationToken ct)
    {
        var conRol = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where a.TenantId == tenantId && a.DeletedAt == null && r.ProductCode == productCode
            select a.UserId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var suspendidos = await db.ProductRoleSuspensions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ProductCode == productCode)
            .Select(s => s.UserId).ToListAsync(ct).ConfigureAwait(false);

        var ids = conRol.Concat(suspendidos).Distinct().ToList();
        if (ids.Count == 0)
            return [];
        var snapshots = await SnapshotsAsync(tenantId, ids, productCode, ct).ConfigureAwait(false);
        return snapshots.Where(s => s.CurrentRole is not null || s.SuspendedRole is not null).ToList();
    }

    public async Task<ProductInvitationSnapshot?> GetPendingInvitationAsync(Guid tenantId, Guid invitationId, string productCode, CancellationToken ct)
    {
        var list = await PendingInvitations(tenantId, productCode).Where(i => i.InvitationId == invitationId)
            .ToListAsync(ct).ConfigureAwait(false);
        return list.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ProductInvitationSnapshot>> ListPendingInvitationsAsync(Guid tenantId, string productCode, CancellationToken ct)
    {
        var list = await PendingInvitations(tenantId, productCode).ToListAsync(ct).ConfigureAwait(false);
        return list.GroupBy(i => i.InvitationId).Select(g => g.First()).ToList();
    }

    /// <remarks>CR-5: «activo» = cuenta activa y sin suspensión temporal vigente en la empresa.</remarks>
    public Task<int> CountOtherActiveAdminsAsync(Guid tenantId, string adminRoleCode, Guid excludingUserId, CancellationToken ct)
    {
        var suspendidas = SuspensionesVigentes();
        return (from a in db.UserRoleAssignments.AsNoTracking()
                join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
                join u in db.Users.AsNoTracking() on a.UserId equals u.Id
                where a.TenantId == tenantId && a.DeletedAt == null && r.Code == adminRoleCode && r.DeletedAt == null
                    && u.DeletedAt == null && u.Status == Active && a.UserId != excludingUserId
                    && !suspendidas.Any(s => s.UserId == a.UserId && s.TenantId == a.TenantId)
                select a.UserId).Distinct().CountAsync(ct);
    }

    public Task ReplaceProductRoleAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, string? lastAdminGuardRoleCode, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            await GuardLastAdminAsync(tenantId, userId, lastAdminGuardRoleCode, ct).ConfigureAwait(false);
            await CloseProductAssignmentsAsync(tenantId, userId, productCode, actorId, ct).ConfigureAwait(false);
            await InsertAssignmentAsync(tenantId, userId, roleId, actorId, ct).ConfigureAwait(false);
        }, ct);

    public Task AddProductRoleAsync(Guid tenantId, Guid userId, Guid roleId, Guid actorId, CancellationToken ct) =>
        InsertAssignmentAsync(tenantId, userId, roleId, actorId, ct);

    public Task SuspendAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, string? lastAdminGuardRoleCode, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            await GuardLastAdminAsync(tenantId, userId, lastAdminGuardRoleCode, ct).ConfigureAwait(false);
            await CloseProductAssignmentsAsync(tenantId, userId, productCode, actorId, ct).ConfigureAwait(false);
            await DeleteMarkerAsync(tenantId, userId, productCode, ct).ConfigureAwait(false);
            db.ProductRoleSuspensions.Add(new ProductRoleSuspensionEntity
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                UserId = userId,
                ProductCode = productCode,
                RoleId = roleId,
                SuspendedAt = DateTimeOffset.UtcNow,
                SuspendedBy = actorId,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }, ct);

    public Task ReactivateAsync(Guid tenantId, Guid userId, string productCode, Guid roleId, Guid actorId, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            await InsertAssignmentAsync(tenantId, userId, roleId, actorId, ct).ConfigureAwait(false);
            await DeleteMarkerAsync(tenantId, userId, productCode, ct).ConfigureAwait(false);
        }, ct);

    public Task ClearSuspensionAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct) =>
        DeleteMarkerAsync(tenantId, userId, productCode, ct);

    public async Task<InviteTarget> ResolveInviteTargetAsync(Guid tenantId, string normalizedEmail, string productCode, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Email == normalizedEmail && u.DeletedAt == null)
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (user is null)
        {
            var pendiente = await db.UserInvitations.AsNoTracking()
                .AnyAsync(i => i.Email == normalizedEmail && i.Status == Pending, ct).ConfigureAwait(false);
            return new InviteTarget(pendiente ? InviteTargetKind.PendingInvitation : InviteTargetKind.NewEmail);
        }

        var member = await GetMemberAsync(tenantId, user.Id, productCode, ct).ConfigureAwait(false);
        if (member is null)
            return new InviteTarget(InviteTargetKind.OtherTenant);
        return member.CurrentRole is not null || member.SuspendedRole is not null
            ? new InviteTarget(InviteTargetKind.MemberWithRole, user.Id)
            : new InviteTarget(InviteTargetKind.MemberWithoutRole, user.Id);
    }

    public async Task<IReadOnlyList<ProductTenantCount>> CountByTenantAsync(string productCode, IReadOnlyCollection<string> roleCodes, string adminRoleCode, CancellationToken ct)
    {
        var suspendidas = SuspensionesVigentes();
        var activos = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            join u in db.Users.AsNoTracking() on a.UserId equals u.Id
            where a.DeletedAt == null && r.ProductCode == productCode && roleCodes.Contains(r.Code) && u.DeletedAt == null
            select new
            {
                a.TenantId,
                a.UserId,
                // CR-5: un admin con suspensión temporal vigente en la empresa no cuenta como administrador activo.
                Admin = r.Code == adminRoleCode && u.Status == Active && !suspendidas.Any(s => s.UserId == a.UserId && s.TenantId == a.TenantId),
            })
            .Distinct().ToListAsync(ct).ConfigureAwait(false);
        var suspendidos = await (
            from s in db.ProductRoleSuspensions.AsNoTracking()
            join r in db.Roles.AsNoTracking() on s.RoleId equals r.Id
            where s.ProductCode == productCode && roleCodes.Contains(r.Code)
            select new { s.TenantId, s.UserId })
            .ToListAsync(ct).ConfigureAwait(false);
        var invitaciones = await (
            from i in db.UserInvitations.AsNoTracking()
            where i.Status == Pending && i.DeletedAt == null
                && (db.InvitationRoles.Any(ir => ir.InvitationId == i.Id && ir.DeletedAt == null
                        && db.Roles.Any(r => r.Id == ir.RoleId && r.ProductCode == productCode && roleCodes.Contains(r.Code)))
                    || db.Roles.Any(r => r.Id == i.RoleId && r.ProductCode == productCode && roleCodes.Contains(r.Code)))
            select new { i.TenantId, i.Id })
            .ToListAsync(ct).ConfigureAwait(false);

        var tenants = activos.Select(a => a.TenantId).Concat(suspendidos.Select(s => s.TenantId)).Concat(invitaciones.Select(i => i.TenantId)).Distinct();
        return tenants.Select(t => new ProductTenantCount(
                t,
                activos.Where(a => a.TenantId == t).Select(a => a.UserId)
                    .Union(suspendidos.Where(s => s.TenantId == t).Select(s => s.UserId)).Count()
                    + invitaciones.Count(i => i.TenantId == t),
                activos.Where(a => a.TenantId == t && a.Admin).Select(a => a.UserId).Distinct().Count()))
            .ToList();
    }

    private IQueryable<UserTempSuspension> SuspensionesVigentes() =>
        db.UserTempSuspensions.AsNoTracking().Where(UserTempSuspension.VigenteEn(DateTimeOffset.UtcNow));

    private IQueryable<ProductInvitationSnapshot> PendingInvitations(Guid tenantId, string productCode) =>
        from i in db.UserInvitations.AsNoTracking()
        where i.TenantId == tenantId && i.Status == Pending && i.DeletedAt == null
        from r in db.Roles.AsNoTracking()
        where r.ProductCode == productCode
            && (db.InvitationRoles.Any(ir => ir.InvitationId == i.Id && ir.RoleId == r.Id && ir.DeletedAt == null) || i.RoleId == r.Id)
        select new ProductInvitationSnapshot(i.Id, i.FullName, i.Email, r.Code);

    /// <summary>Snapshot de miembros de la empresa en lote (sin N+1).</summary>
    private async Task<List<ProductMemberSnapshot>> SnapshotsAsync(Guid tenantId, IReadOnlyCollection<Guid> userIds, string productCode, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.DeletedAt == null)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Status, u.HomeTenantId })
            .ToListAsync(ct).ConfigureAwait(false);
        if (users.Count == 0)
            return [];

        var ids = users.Select(u => u.Id).ToList();
        var asignaciones = await (
            from a in db.UserRoleAssignments.AsNoTracking()
            join r in db.Roles.AsNoTracking() on a.RoleId equals r.Id
            where ids.Contains(a.UserId) && a.DeletedAt == null && r.DeletedAt == null
            select new { a.UserId, a.TenantId, a.RoleId, r.Code, r.ProductCode })
            .ToListAsync(ct).ConfigureAwait(false);
        var marcas = await (
            from s in db.ProductRoleSuspensions.AsNoTracking()
            join r in db.Roles.AsNoTracking() on s.RoleId equals r.Id
            where s.TenantId == tenantId && s.ProductCode == productCode && ids.Contains(s.UserId)
            select new { s.UserId, s.RoleId, r.Code })
            .ToListAsync(ct).ConfigureAwait(false);
        var suspendidosCuenta = await db.UserTempSuspensions.AsNoTracking()
            .Where(UserTempSuspension.VigenteEn(ahora))
            .Where(s => s.TenantId == tenantId && ids.Contains(s.UserId))
            .Select(s => s.UserId).Distinct().ToListAsync(ct).ConfigureAwait(false);

        var result = new List<ProductMemberSnapshot>(users.Count);
        foreach (var u in users)
        {
            var enEmpresa = asignaciones.Where(a => a.UserId == u.Id && a.TenantId == tenantId).ToList();
            var marca = marcas.FirstOrDefault(m => m.UserId == u.Id);
            if (u.HomeTenantId != tenantId && enEmpresa.Count == 0 && marca is null)
                continue;

            var actual = enEmpresa.FirstOrDefault(a => a.ProductCode == productCode);
            result.Add(new ProductMemberSnapshot(
                u.Id,
                u.DisplayName,
                u.Email,
                AccountActive: u.Status == Active && !suspendidosCuenta.Contains(u.Id),
                IsAdminCompany: enEmpresa.Any(a => a.Code == AdminCompanyRole),
                IsSuperAdmin: asignaciones.Any(a => a.UserId == u.Id && a.Code == SuperAdminRole),
                CurrentRole: actual is null ? null : new ProductRoleRef(actual.RoleId, actual.Code),
                SuspendedRole: marca is null ? null : new ProductRoleRef(marca.RoleId, marca.Code)));
        }

        return result;
    }

    /// <summary>
    /// CR-4 — guarda atómica del último admin: bloquea (<c>FOR UPDATE</c>) las asignaciones vigentes del rol admin en la
    /// empresa y, dentro de la misma transacción, exige otro admin activo. Dos bajas simultáneas de los dos últimos admins
    /// se serializan en el bloqueo y la segunda ve la primera ya confirmada.
    /// </summary>
    private async Task GuardLastAdminAsync(Guid tenantId, Guid userId, string? adminRoleCode, CancellationToken ct)
    {
        if (adminRoleCode is null)
            return;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT a.id FROM security.user_role_assignments a JOIN security.roles r ON r.id = a.role_id
             WHERE a.tenant_id = {tenantId} AND a.deleted_at IS NULL AND r.code = {adminRoleCode} AND r.deleted_at IS NULL
             FOR UPDATE OF a
            """, ct).ConfigureAwait(false);
        if (await CountOtherActiveAdminsAsync(tenantId, adminRoleCode, userId, ct).ConfigureAwait(false) <= 0)
            throw new ProductRoleRuleException(ProductRoleErrorCodes.UltimoAdministrador, ProductRoleErrorKind.FailedPrecondition, "La compañía quedaría sin administrador del producto.");
    }

    private Task<int> CloseProductAssignmentsAsync(Guid tenantId, Guid userId, string productCode, Guid actorId, CancellationToken ct) =>
        db.UserRoleAssignments
            .Where(a => a.UserId == userId && a.TenantId == tenantId && a.DeletedAt == null
                && db.Roles.Any(r => r.Id == a.RoleId && r.ProductCode == productCode))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.DeletedAt, DateTimeOffset.UtcNow)
                .SetProperty(a => a.DeletedBy, actorId), ct);

    private async Task InsertAssignmentAsync(Guid tenantId, Guid userId, Guid roleId, Guid actorId, CancellationToken ct)
    {
        var ahora = DateTimeOffset.UtcNow;
        db.UserRoleAssignments.Add(new UserRoleAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            UserId = userId,
            RoleId = roleId,
            AssignedAt = ahora,
            AssignedBy = actorId,
            CreatedAt = ahora,
            CreatedBy = actorId,
            RowVersion = 0,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private Task<int> DeleteMarkerAsync(Guid tenantId, Guid userId, string productCode, CancellationToken ct) =>
        db.ProductRoleSuspensions
            .Where(s => s.TenantId == tenantId && s.UserId == userId && s.ProductCode == productCode)
            .ExecuteDeleteAsync(ct);

    /// <summary>Con <c>EnableRetryOnFailure</c> la transacción va dentro de la estrategia de ejecución.</summary>
    private Task InTransactionAsync(Func<Task> work, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            return work();

        var strategy = db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                await work().ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        });
    }
}
