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

            // Timeline de negocio: pre-trámite recibido (detail por allowlist, sin PII).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_pretramite_event({master.Id}, {tenantId}, 'recibido', 'ok',
                    jsonb_build_object('transaction_type', {master.TransactionType},
                                       'manager_id_transaction', {master.ManagerIdTransaction},
                                       'traffic_secretary_code', {master.TrafficSecretaryCode}))
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

    public Task MarkAbortedAsync(
        Guid masterId,
        Guid tenantId,
        string observation,
        string user,
        string mail,
        string company,
        CancellationToken ct = default) =>
        InTenantTransactionAsync(tenantId, async () =>
        {
            // El trámite ya fue anulado en core-api (autoridad). Aquí solo se refleja en el pre-trámite
            // y su histórico para que el endpoint de estado v1 muestre 'ANULADO' (6).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE ict.external_integration_master
                SET process_status_id = 6, updated_at = now()
                WHERE id = {masterId} AND tenant_id = {tenantId};
                SELECT ict.record_process_status({masterId}, {tenantId}, 6, 'ANULADO',
                    {user}, {mail}, {company}, {observation}, 'integration')
                """, ct);

            // Timeline: anulado. NO se incluye la observation (texto libre del cliente → riesgo PII).
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_pretramite_event({masterId}, {tenantId}, 'anulado', 'ok')
                """, ct);
            return 0;
        }, ct);

    public Task RecordTimelineEventAsync(
        Guid masterId,
        Guid tenantId,
        string stage,
        string outcome,
        string? detailJson,
        CancellationToken ct = default) =>
        InTenantTransactionAsync(tenantId, async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT ict.record_pretramite_event({masterId}, {tenantId}, {stage}, {outcome}, {detailJson}::jsonb)
                """, ct);
            return 0;
        }, ct);

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
