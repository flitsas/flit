using Flit.Modules.Security.Domain.PermissionOverrides;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas para los permisos efectivos del token (HU #13643): permisos individuales VIGENTES del usuario en el tenant
/// del token (una sola consulta, con el slug resuelto por JOIN: sin N+1) y productos habilitados de la empresa.
/// SQL parametrizado sobre <see cref="IIdentityDb"/> para que lo compartan core-api y core-identity (la entidad de los
/// overrides vive en el contexto de core-api).
/// </summary>
internal sealed class EffectivePermissionOverrideReader(IIdentityDb db)
{
    private sealed record OverrideRow(string Slug, string ProductCode, string Effect);

    public async Task<IReadOnlyList<EffectivePermissionOverride>> ListActiveAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            return [];
        }

        return await TenantRlsScope.ExecuteAsync(
            db.Database,
            tenantId,
            async () =>
            {
                var rows = await db.Database.SqlQuery<OverrideRow>($"""
                    SELECT p.slug AS slug, o.product_code AS product_code, o.effect AS effect
                    FROM security.user_permission_overrides o
                    JOIN security.permissions p ON p.id = o.permission_id
                    WHERE o.tenant_id = {tenantId} AND o.user_id = {userId} AND o.deleted_at IS NULL
                      AND p.is_active AND p.deleted_at IS NULL
                    """).ToListAsync(ct).ConfigureAwait(false);
                return (IReadOnlyList<EffectivePermissionOverride>)rows
                    .Select(r => new EffectivePermissionOverride(r.Slug, r.ProductCode, r.Effect))
                    .ToList();
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Productos encendidos para la empresa Y para todos sus ancestros (regla fail-closed de ADR-0057, igual que
    /// <c>ProductAccessResolver</c>), en una consulta con CTE recursiva acotada a 8 niveles.
    /// </summary>
    public async Task<IReadOnlySet<string>> EnabledProductsAsync(Guid tenantId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var codes = await db.Database.SqlQuery<string>($"""
            WITH RECURSIVE chain AS (
                SELECT id, parent_tenant_id, 1 AS depth FROM identity.tenants WHERE id = {tenantId}
                UNION ALL
                SELECT t.id, t.parent_tenant_id, c.depth + 1
                FROM identity.tenants t JOIN chain c ON t.id = c.parent_tenant_id
                WHERE c.depth < 8
            )
            SELECT tp.product_code AS "Value"
            FROM platform.tenant_products tp JOIN chain c ON c.id = tp.tenant_id
            WHERE tp.enabled
            GROUP BY tp.product_code
            HAVING count(*) = (SELECT count(*) FROM chain)
            """).ToListAsync(ct).ConfigureAwait(false);
        return codes.ToHashSet(StringComparer.Ordinal);
    }
}
