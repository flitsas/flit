using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>Persistencia EF de adjuntos del pre-trámite (RLS por tenant; transacción dentro de la execution strategy).</summary>
public sealed class AttachmentRepository(IctDbContext db) : IAttachmentRepository
{
    public Task AddAsync(TransactionAttachment attachment, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return InTenantTransactionAsync(tenantId, async () =>
        {
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
    }

    public async Task<IReadOnlyList<TransactionAttachment>> ListAsync(
        Guid masterId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var list = await InTenantTransactionAsync(
            tenantId,
            () => db.Attachments
                .Where(a => a.MasterId == masterId && a.DeletedAt == null)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct),
            ct);
        return list;
    }

    public async Task<IReadOnlyList<string>> MissingMandatoryDocumentsAsync(
        Guid masterId,
        Guid tenantId,
        short transactionType,
        CancellationToken ct = default)
    {
        var missing = await InTenantTransactionAsync(tenantId, () =>
        {
            var mandatory = db.ConfigurationDocuments
                .Where(c => c.ExternalTransactionType == transactionType && c.IsMandatory);
            var present = db.Attachments
                .Where(a => a.MasterId == masterId && a.DeletedAt == null)
                .Select(a => a.IdAttachment);
            return mandatory
                .Where(c => !present.Contains(c.IdEiad))
                .Join(db.AllowedDocuments, c => c.IdEiad, d => d.Id, (c, d) => d.Name)
                .ToListAsync(ct);
        }, ct);
        return missing;
    }

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
