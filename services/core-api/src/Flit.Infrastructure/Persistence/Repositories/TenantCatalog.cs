using Flit.Admin.Domain.DocumentOrderOverrides;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de <see cref="ITenantCatalog"/> (HU #10196). Consulta de solo
/// lectura sobre <c>identity.tenants</c> para validar la existencia de un cliente al
/// registrar un override de scope <c>CLIENTE</c>.
/// </summary>
internal sealed class TenantCatalog : ITenantCatalog
{
    private readonly FlitDbContext _context;

    public TenantCatalog(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> ExistsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        _context.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == tenantId, cancellationToken);
}
