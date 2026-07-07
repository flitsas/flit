using Flit.Admin.Domain.CompanyDocumentParams;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de <see cref="ICompanyDocumentParamRepository"/> (HU #10521, RF31).
/// Consultas EF LINQ parametrizadas; upsert por (tenant_id, document_type_code).
/// </summary>
internal sealed class CompanyDocumentParamRepository(FlitDbContext db) : ICompanyDocumentParamRepository
{
    public async Task<IReadOnlyList<CompanyDocumentParamItem>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        await db.CompanyDocumentParams
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderBy(p => p.DocumentTypeCode)
            .Select(p => new CompanyDocumentParamItem(p.Id, p.TenantId, p.DocumentTypeCode, p.State))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<CompanyDocumentParamItem> UpsertAsync(
        Guid tenantId,
        string documentTypeCode,
        string state,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var entity = await db.CompanyDocumentParams
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.DocumentTypeCode == documentTypeCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new CompanyDocumentParamEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DocumentTypeCode = documentTypeCode,
                State = state,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId,
            };
            db.CompanyDocumentParams.Add(entity);
        }
        else
        {
            entity.State = state;
            entity.UpdatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedBy = userId;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new CompanyDocumentParamItem(entity.Id, entity.TenantId, entity.DocumentTypeCode, entity.State);
    }
}
