using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras EF Core de escrituras (HU #10900, ADR-0033). Tenant-scoped: cada operación fija
/// <c>app.current_tenant_id</c> (<see cref="TenantRlsScope"/>). Las invariantes de vigencia se
/// validan vía el agregado <see cref="Deed"/> antes de persistir.
/// </summary>
internal sealed class DeedRepository : IDeedRepository
{
    private readonly FlitDbContext _context;

    public DeedRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Guid> CreateAsync(SaveDeedData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data.StoragePath) || string.IsNullOrWhiteSpace(data.StorageSha256))
        {
            throw new ArgumentException("El artefacto (path + hash) es obligatorio al crear una escritura.", nameof(data));
        }

        return TenantRlsScope.ExecuteAsync(
            _context,
            data.TenantId,
            async () =>
            {
                var deed = Deed.Create(
                    data.TenantId, data.Description, data.StoragePath!, data.StorageSha256!,
                    data.VigenciaDesde, data.VigenciaHasta, data.RepresentativeId);

                var entity = new CompanyDeedEntity
                {
                    Id = deed.Id,
                    TenantId = deed.TenantId,
                    RepresentativeId = deed.RepresentativeId,
                    Description = deed.Description,
                    StoragePath = deed.StoragePath,
                    StorageSha256 = deed.StorageSha256,
                    VigenciaDesde = deed.VigenciaDesde,
                    VigenciaHasta = deed.VigenciaHasta,
                    IsActive = deed.IsActive,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = data.ActorBy,
                };

                _context.CompanyDeeds.Add(entity);
                AddCompanies(entity.Id, data.RepresentedCompanyIds);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return entity.Id;
            },
            cancellationToken);
    }

    public Task<bool> UpdateAsync(SaveDeedData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Id is not { } id)
        {
            throw new ArgumentException("La edición requiere el id de la escritura.", nameof(data));
        }

        return TenantRlsScope.ExecuteAsync(
            _context,
            data.TenantId,
            async () =>
            {
                var entity = await _context.CompanyDeeds
                    .FirstOrDefaultAsync(d => d.TenantId == data.TenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return false;
                }

                var deed = Deed.Rehydrate(
                    entity.Id, entity.TenantId, entity.Description, entity.StoragePath, entity.StorageSha256,
                    entity.VigenciaDesde, entity.VigenciaHasta, entity.IsActive);
                deed.UpdateDetails(data.Description, data.VigenciaDesde, data.VigenciaHasta);
                if (!string.IsNullOrWhiteSpace(data.StoragePath) && !string.IsNullOrWhiteSpace(data.StorageSha256))
                {
                    deed.ReplaceArtifact(data.StoragePath!, data.StorageSha256!);
                }

                entity.Description = deed.Description;
                entity.VigenciaDesde = deed.VigenciaDesde;
                entity.VigenciaHasta = deed.VigenciaHasta;
                entity.StoragePath = deed.StoragePath;
                entity.StorageSha256 = deed.StorageSha256;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                entity.UpdatedBy = data.ActorBy;

                await ReplaceCompaniesAsync(entity.Id, cancellationToken).ConfigureAwait(false);
                AddCompanies(entity.Id, data.RepresentedCompanyIds);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid id,
        Guid? changedBy,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyDeeds
                    .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null || !entity.IsActive)
                {
                    return false;
                }

                entity.IsActive = false;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                entity.UpdatedBy = changedBy;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    private async Task ReplaceCompaniesAsync(Guid deedId, CancellationToken cancellationToken)
    {
        var existing = await _context.CompanyDeedCompanies
            .Where(dc => dc.DeedId == deedId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count > 0)
        {
            _context.CompanyDeedCompanies.RemoveRange(existing);
        }
    }

    private void AddCompanies(Guid deedId, IReadOnlyList<Guid> representedCompanyIds)
    {
        foreach (var companyId in representedCompanyIds.Distinct())
        {
            _context.CompanyDeedCompanies.Add(new CompanyDeedCompanyEntity
            {
                Id = Guid.NewGuid(),
                DeedId = deedId,
                RepresentedCompanyId = companyId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}
