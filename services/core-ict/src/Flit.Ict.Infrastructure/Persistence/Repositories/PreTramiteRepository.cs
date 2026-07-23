using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia EF de pre-trámites con aislamiento por tenant (RLS). Cada unidad de trabajo abre una
/// transacción y fija el GUC <c>app.current_tenant_id</c> (local a la transacción) para que las
/// políticas de RLS apliquen. En dev el usuario es superusuario (RLS se omite); el GUC deja el
/// comportamiento correcto para producción con un rol no-superusuario.
/// </summary>
public sealed class PreTramiteRepository(IctDbContext db) : IPreTramiteRepository
{
    public async Task<Guid> AddAsync(ExternalIntegrationMaster master, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantGucAsync(tenantId, ct);
        db.Masters.Add(master);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return master.Id;
    }

    public async Task<ExternalIntegrationMaster?> GetAsync(Guid id, Guid tenantId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantGucAsync(tenantId, ct);
        var master = await db.Masters
            .Include(m => m.Actors)
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, ct);
        await tx.CommitAsync(ct);
        return master;
    }

    public async Task SaveAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetTenantGucAsync(tenantId, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new IctConcurrencyException("Conflicto de row_version al editar el pre-trámite.", ex);
        }
    }

    private Task<int> SetTenantGucAsync(Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct);
}
