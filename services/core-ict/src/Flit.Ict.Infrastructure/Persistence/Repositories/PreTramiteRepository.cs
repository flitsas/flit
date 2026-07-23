using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia EF de pre-trámites con aislamiento por tenant (RLS). Cada unidad de trabajo abre una
/// transacción y fija el GUC <c>app.current_tenant_id</c> (local a la transacción). Como el DbContext
/// tiene EnableRetryOnFailure, la transacción va DENTRO de la execution strategy de EF (requisito para
/// combinar reintentos + transacciones iniciadas por el usuario).
/// </summary>
public sealed class PreTramiteRepository(IctDbContext db) : IPreTramiteRepository
{
    public Task<Guid> AddAsync(ExternalIntegrationMaster master, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(master);
        return InTenantTransactionAsync(tenantId, async () =>
        {
            db.Masters.Add(master);
            await db.SaveChangesAsync(ct);

            // Primer estado del histórico = Registrado (1), como en v1 (statusProcess[]). El registrador
            // colapsa sub-pasos por estado; la fila Registrado de v1 va con rol vacío (observation='' rol='').
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_process_status({master.Id}, {tenantId}, 1, 'REGISTRADO',
                    {master.ManagerUser}, {master.ManagerMail}, {master.CompanyManagerDocument}, '', '')
                """, ct);
            return master.Id;
        }, ct);
    }

    public Task<ExternalIntegrationMaster?> GetAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        InTenantTransactionAsync(
            tenantId,
            () => db.Masters.Include(m => m.Actors).FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null, ct),
            ct);

    public Task<ExternalIntegrationMaster?> FindByManagerIdTransactionAsync(
        string managerIdTransaction,
        Guid tenantId,
        CancellationToken ct = default) =>
        InTenantTransactionAsync(
            tenantId,
            () => db.Masters
                .Include(m => m.Actors)
                .FirstOrDefaultAsync(m => m.ManagerIdTransaction == managerIdTransaction && m.DeletedAt == null, ct),
            ct);

    public async Task SaveAsync(Guid tenantId, CancellationToken ct = default)
    {
        try
        {
            await InTenantTransactionAsync(tenantId, async () =>
            {
                await db.SaveChangesAsync(ct);
                return 0;
            }, ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new IctConcurrencyException("Conflicto de row_version al editar el pre-trámite.", ex);
        }
    }

    /// <summary>Ejecuta <paramref name="work"/> en una transacción con el GUC de tenant fijado, dentro de la execution strategy.</summary>
    private async Task<T> InTenantTransactionAsync<T>(Guid tenantId, Func<Task<T>> work, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });
    }
}
