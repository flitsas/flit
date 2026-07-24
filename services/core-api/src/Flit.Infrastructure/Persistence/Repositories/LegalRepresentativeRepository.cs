using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras EF Core del directorio de representantes legales por compañía (HU #10900, ADR-0033).
/// Tenant-scoped: cada operación fija <c>app.current_tenant_id</c> (<see cref="TenantRlsScope"/>),
/// igual que el baúl de firmas. Las invariantes de datos se validan vía los agregados del dominio
/// (<see cref="RepresentedCompany"/>/<see cref="LegalRepresentative"/>) antes de persistir; la
/// unicidad del par (compañía, representante) por tenant la garantiza el índice único de BD.
/// <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
internal sealed class LegalRepresentativeRepository : ILegalRepresentativeRepository
{
    private readonly FlitDbContext _context;

    public LegalRepresentativeRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Guid> UpsertRepresentedCompanyAsync(
        UpsertRepresentedCompanyData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var nit = data.DocumentNumber.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            data.TenantId,
            async () =>
            {
                var existing = await _context.RepresentedCompanies
                    .FirstOrDefaultAsync(
                        c => c.TenantId == data.TenantId && c.DocumentNumber == nit, cancellationToken)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    // Rehidrata + aplica invariantes de actualización antes de tocar la fila.
                    var aggregate = RepresentedCompany.Rehydrate(
                        existing.Id, existing.TenantId, existing.DocumentType, existing.DocumentNumber,
                        existing.Name, existing.Email, existing.Address, existing.City, existing.Phone);
                    aggregate.UpdateDetails(data.Name, data.Email, data.Address, data.City, data.Phone);

                    existing.Name = aggregate.Name;
                    existing.Email = aggregate.Email;
                    existing.Address = aggregate.Address;
                    existing.City = aggregate.City;
                    existing.Phone = aggregate.Phone;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    existing.UpdatedBy = data.ActorBy;

                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return existing.Id;
                }

                var company = RepresentedCompany.Create(
                    data.TenantId, nit, data.Name, data.Email, data.Address, data.City, data.Phone);

                var entity = new RepresentedCompanyEntity
                {
                    Id = company.Id,
                    TenantId = company.TenantId,
                    DocumentType = company.DocumentType,
                    DocumentNumber = company.DocumentNumber,
                    Name = company.Name,
                    Email = company.Email,
                    Address = company.Address,
                    City = company.City,
                    Phone = company.Phone,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = data.ActorBy,
                };

                _context.RepresentedCompanies.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return entity.Id;
            },
            cancellationToken);
    }

    public Task<Guid> SaveAsync(SaveLegalRepresentativeData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return TenantRlsScope.ExecuteAsync(
            _context,
            data.TenantId,
            async () =>
            {
                var entity = data.Id is { } id
                    ? await _context.CompanyLegalRepresentatives
                        .FirstOrDefaultAsync(
                            r => r.TenantId == data.TenantId && r.Id == id, cancellationToken)
                        .ConfigureAwait(false)
                    : null;

                if (data.Id is not null && entity is null)
                {
                    throw new InvalidOperationException("El representante a editar no existe en el tenant.");
                }

                if (entity is null)
                {
                    var aggregate = LegalRepresentative.Create(
                        data.TenantId, data.RepresentedCompanyId, data.DocumentType, data.DocumentNumber,
                        data.FirstLastName, data.Name, data.SecondLastName, data.Email, data.Address,
                        data.City, data.Phone);
                    ApplyResolution(aggregate, data);

                    entity = new CompanyLegalRepresentativeEntity
                    {
                        Id = aggregate.Id,
                        TenantId = aggregate.TenantId,
                        RepresentedCompanyId = aggregate.RepresentedCompanyId,
                        DocumentType = aggregate.DocumentType,
                        DocumentNumber = aggregate.DocumentNumber,
                        FirstLastName = aggregate.FirstLastName,
                        SecondLastName = aggregate.SecondLastName,
                        Name = aggregate.Name,
                        Email = aggregate.Email,
                        Address = aggregate.Address,
                        City = aggregate.City,
                        Phone = aggregate.Phone,
                        SignatureVaultId = aggregate.SignatureVaultId,
                        IdentityValidationRef = aggregate.IdentityValidationRef,
                        IsActive = aggregate.IsActive,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CreatedBy = data.ActorBy,
                    };
                    _context.CompanyLegalRepresentatives.Add(entity);
                }
                else
                {
                    var aggregate = LegalRepresentative.Rehydrate(
                        entity.Id, entity.TenantId, entity.RepresentedCompanyId, entity.DocumentType,
                        entity.DocumentNumber, entity.FirstLastName, entity.SecondLastName, entity.Name,
                        entity.Email, entity.Address, entity.City, entity.Phone, entity.SignatureVaultId,
                        entity.IdentityValidationRef, entity.IsActive);
                    aggregate.UpdateDetails(
                        data.FirstLastName, data.Name, data.SecondLastName, data.Email, data.Address,
                        data.City, data.Phone);
                    ApplyResolution(aggregate, data);

                    entity.FirstLastName = aggregate.FirstLastName;
                    entity.SecondLastName = aggregate.SecondLastName;
                    entity.Name = aggregate.Name;
                    entity.Email = aggregate.Email;
                    entity.Address = aggregate.Address;
                    entity.City = aggregate.City;
                    entity.Phone = aggregate.Phone;
                    entity.SignatureVaultId = aggregate.SignatureVaultId;
                    entity.IdentityValidationRef = aggregate.IdentityValidationRef;
                    entity.UpdatedAt = DateTimeOffset.UtcNow;
                    entity.UpdatedBy = data.ActorBy;

                    await ReplaceProcedureTypesAsync(entity.Id, cancellationToken).ConfigureAwait(false);
                }

                AddProcedureTypes(entity.Id, data.ProcedureTypeIds);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return entity.Id;
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
                var entity = await _context.CompanyLegalRepresentatives
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == id, cancellationToken)
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

    private static void ApplyResolution(LegalRepresentative aggregate, SaveLegalRepresentativeData data)
    {
        if (data.SignatureVaultId is { } signatureId)
        {
            aggregate.LinkSignature(signatureId);
        }
        else if (data.IdentityValidationRef is { } identityRef)
        {
            aggregate.LinkIdentity(identityRef);
        }
        else
        {
            aggregate.ClearSignatureAndIdentity();
        }
    }

    private async Task ReplaceProcedureTypesAsync(Guid representativeId, CancellationToken cancellationToken)
    {
        var existing = await _context.CompanyLegalRepresentativeProcedureTypes
            .Where(p => p.RepresentativeId == representativeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count > 0)
        {
            _context.CompanyLegalRepresentativeProcedureTypes.RemoveRange(existing);
        }
    }

    private void AddProcedureTypes(Guid representativeId, IReadOnlyList<Guid> procedureTypeIds)
    {
        foreach (var procedureTypeId in procedureTypeIds.Distinct())
        {
            _context.CompanyLegalRepresentativeProcedureTypes.Add(new CompanyLegalRepresentativeProcedureTypeEntity
            {
                Id = Guid.NewGuid(),
                RepresentativeId = representativeId,
                ProcedureTypeId = procedureTypeId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}
