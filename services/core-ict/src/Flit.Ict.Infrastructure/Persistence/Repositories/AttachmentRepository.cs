using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>Persistencia EF de adjuntos del pre-trámite (RLS por tenant, transacción con GUC).</summary>
public sealed class AttachmentRepository(IctDbContext db) : IAttachmentRepository
{
    public async Task AddAsync(TransactionAttachment attachment, Guid tenantId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetGucAsync(tenantId, ct);
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<TransactionAttachment>> ListAsync(
        Guid masterId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetGucAsync(tenantId, ct);
        var list = await db.Attachments
            .Where(a => a.MasterId == masterId && a.DeletedAt == null)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        await tx.CommitAsync(ct);
        return list;
    }

    public async Task<IReadOnlyList<string>> MissingMandatoryDocumentsAsync(
        Guid masterId,
        Guid tenantId,
        short transactionType,
        CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await SetGucAsync(tenantId, ct);

        var mandatory = db.ConfigurationDocuments
            .Where(c => c.ExternalTransactionType == transactionType && c.IsMandatory);

        var present = db.Attachments
            .Where(a => a.MasterId == masterId && a.DeletedAt == null)
            .Select(a => a.IdAttachment);

        var missing = await mandatory
            .Where(c => !present.Contains(c.IdEiad))
            .Join(db.AllowedDocuments, c => c.IdEiad, d => d.Id, (c, d) => d.Name)
            .ToListAsync(ct);

        await tx.CommitAsync(ct);
        return missing;
    }

    private Task<int> SetGucAsync(Guid tenantId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)", ct);
}
