using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Modules.Platform.Domain.Manifest;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// Aplica el manifiesto de un producto al RBAC (HU #12966, contrato v1 §6) en una sola transacción.
/// Crea o actualiza módulos por <c>code</c>, permisos por <c>slug</c> y roles por defecto por <c>code</c> en
/// <c>COMPANY</c>, todos con el <c>product_code</c> del manifiesto. No borra nada: lo que el SuperAdmin haya
/// agregado a mano a un rol por defecto se conserva.
/// </summary>
internal sealed class ProductManifestStore : IProductManifestStore
{
    /// <summary>
    /// El manifiesto no trae método ni ruta (el contrato §6 solo pide slug y nombre). Las columnas son
    /// obligatorias, así que los permisos de manifiesto se guardan sin ruta: los productos nuevos autorizan
    /// por slug, no por <c>route_pattern</c>.
    /// </summary>
    private const string ManifestHttpMethod = "ANY";

    private readonly FlitDbContext _context;

    public ProductManifestStore(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<ManifestApplyResult> ApplyAsync(ProductManifest manifest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // EnableRetryOnFailure no admite transacciones abiertas a mano fuera de la estrategia de ejecución.
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            var tx = await _context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                var result = await ApplyCoreAsync(manifest, ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }

    private async Task<ManifestApplyResult> ApplyCoreAsync(ProductManifest manifest, CancellationToken ct)
    {
        var product = manifest.ProductCode;
        var now = DateTimeOffset.UtcNow;
        int modulesCreated = 0, permissionsCreated = 0, rolesCreated = 0;

        var permissionIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var m in manifest.Modules)
        {
            var module = await _context.SecurityModules.FirstOrDefaultAsync(x => x.Code == m.Code, ct).ConfigureAwait(false);
            if (module is null)
            {
                module = new SecurityModule { Id = Guid.CreateVersion7(), Code = m.Code, Name = m.Name, ProductCode = product, IsActive = true, CreatedAt = now };
                _context.SecurityModules.Add(module);
                modulesCreated++;
            }
            else if (module.ProductCode != product)
            {
                throw new ManifestConflictException($"El módulo '{m.Code}' ya existe en el producto '{module.ProductCode}'.");
            }
            else
            {
                module.Name = m.Name;
                module.IsActive = true;
                module.DeletedAt = null;
            }

            foreach (var p in m.Permissions)
            {
                var permission = await _context.RbacActions.FirstOrDefaultAsync(x => x.Slug == p.Slug, ct).ConfigureAwait(false);
                if (permission is null)
                {
                    permission = new RbacAction
                    {
                        Id = Guid.CreateVersion7(), ModuleId = module.Id, Slug = p.Slug, Name = p.Name,
                        HttpMethod = ManifestHttpMethod, RoutePattern = string.Empty, IsActive = true, CreatedAt = now,
                    };
                    _context.RbacActions.Add(permission);
                    permissionsCreated++;
                }
                else
                {
                    // El prefijo <producto>. ya lo validó el handler; el módulo tiene que ser del mismo producto.
                    permission.Name = p.Name;
                    permission.ModuleId = module.Id;
                    permission.IsActive = true;
                    permission.DeletedAt = null;
                }

                permissionIds[p.Slug] = permission.Id;
            }
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        foreach (var r in manifest.DefaultRoles)
        {
            var role = await _context.Roles
                .FirstOrDefaultAsync(x => x.Code == r.Code && x.TargetEntityType == "COMPANY" && x.DeletedAt == null, ct)
                .ConfigureAwait(false);
            if (role is null)
            {
                role = new Role { Id = Guid.CreateVersion7(), Code = r.Code, Name = r.Name, TargetEntityType = "COMPANY", ProductCode = product, IsSystem = true, IsActive = true, CreatedAt = now };
                _context.Roles.Add(role);
                rolesCreated++;
            }
            else if (role.ProductCode != product)
            {
                throw new ManifestConflictException($"El rol '{r.Code}' ya existe en el producto '{role.ProductCode}'.");
            }
            else
            {
                role.Name = r.Name;
            }

            var wanted = r.Permissions.Select(s => permissionIds[s]).ToHashSet();
            var granted = await _context.RoleGrants.Where(g => g.RoleId == role.Id).Select(g => g.PermissionId).ToListAsync(ct).ConfigureAwait(false);
            foreach (var permissionId in wanted.Except(granted))
            {
                _context.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role.Id, PermissionId = permissionId, CreatedAt = now });
            }
        }

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        return new ManifestApplyResult(modulesCreated, permissionsCreated, rolesCreated);
    }
}
