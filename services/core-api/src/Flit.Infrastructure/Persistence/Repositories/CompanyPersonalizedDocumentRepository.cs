using Flit.Admin.Application.Companies.PersonalizedDocuments;
using Flit.Admin.Domain.Companies.PersonalizedDocuments;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio EF Core de versiones de documento personalizado (HU #11313, ADR-0042). <b>Toda</b>
/// consulta filtra <c>tenant_id</c> de forma EXPLÍCITA en el <c>Where</c> — el RLS de
/// <c>admin.company_personalized_documents</c> es decorativo (sin <c>FORCE ROW LEVEL SECURITY</c> y
/// con la aplicación como owner, las políticas no se evalúan), así que un filtro olvidado devolvería
/// la fila de cualquier tenant. Mismo patrón que <c>DeedRepository</c> (<see cref="TenantRlsScope"/>).
/// </summary>
internal sealed class CompanyPersonalizedDocumentRepository : ICompanyPersonalizedDocumentRepository
{
    private readonly FlitDbContext _context;

    public CompanyPersonalizedDocumentRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<int> GetNextVersionAsync(Guid tenantId, string documentType, CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var max = await _context.CompanyPersonalizedDocuments
                    .Where(d => d.TenantId == tenantId && d.DocumentType == documentType)
                    .Select(d => (int?)d.Version)
                    .MaxAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (max ?? 0) + 1;
            },
            cancellationToken);

    public Task<Guid> CreatePendingAsync(SaveCompanyPersonalizedDocumentData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return TenantRlsScope.ExecuteAsync(
            _context,
            data.TenantId,
            async () =>
            {
                var entity = new CompanyPersonalizedDocumentEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = data.TenantId,
                    DocumentType = data.DocumentType,
                    Version = data.Version,
                    Status = CompanyPersonalizedDocumentStatus.Pendiente,
                    IsActive = false,
                    Filename = data.Filename,
                    StoragePath = data.StoragePath,
                    StorageSha256 = data.StorageSha256Declared,
                    SizeBytes = data.SizeBytesDeclared,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = data.CreatedBy,
                };

                _context.CompanyPersonalizedDocuments.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return entity.Id;
            },
            cancellationToken);
    }

    public Task<CompanyPersonalizedDocumentRecord?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyPersonalizedDocuments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : Map(entity);
            },
            cancellationToken);

    public Task<IReadOnlyList<CompanyPersonalizedDocumentRecord>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entities = await _context.CompanyPersonalizedDocuments
                    .AsNoTracking()
                    .Where(d => d.TenantId == tenantId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return (IReadOnlyList<CompanyPersonalizedDocumentRecord>)entities.Select(Map).ToList();
            },
            cancellationToken);

    public Task ActivateAsync(
        Guid tenantId,
        Guid id,
        ConfirmCompanyPersonalizedDocumentData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyPersonalizedDocuments
                    .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return 0;
                }

                var now = DateTimeOffset.UtcNow;

                // Retira (a histórico) la activa previa del mismo tipo — nunca se borra (restricción 9).
                var previousActive = await _context.CompanyPersonalizedDocuments
                    .Where(d => d.TenantId == tenantId
                        && d.DocumentType == entity.DocumentType
                        && d.IsActive
                        && d.Id != id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var previous in previousActive)
                {
                    previous.IsActive = false;
                    previous.Status = CompanyPersonalizedDocumentStatus.Historico;
                    previous.DeactivatedAt = now;
                    previous.DeactivatedBy = data.ActivatedBy;
                    previous.UpdatedAt = now;
                    previous.UpdatedBy = data.ActivatedBy;
                }

                entity.Status = CompanyPersonalizedDocumentStatus.Activo;
                entity.IsActive = true;
                entity.StorageSha256 = data.StorageSha256;
                entity.SizeBytes = data.SizeBytes;
                entity.PageCount = data.PageCount;
                entity.ActivatedAt = now;
                entity.ActivatedBy = data.ActivatedBy;
                entity.UpdatedAt = now;
                entity.UpdatedBy = data.ActivatedBy;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return 1;
            },
            cancellationToken);
    }

    public Task RejectAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyPersonalizedDocuments
                    .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return 0;
                }

                entity.Status = CompanyPersonalizedDocumentStatus.Rechazado;
                entity.IsActive = false;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return 1;
            },
            cancellationToken);

    public Task<ReactivateCompanyPersonalizedDocumentResult> ReactivateAsync(
        Guid tenantId,
        Guid id,
        Guid? reactivatedBy,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyPersonalizedDocuments
                    .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return new ReactivateCompanyPersonalizedDocumentResult(
                        PersonalizedDocumentReactivationOutcome.NotFound, null);
                }

                if (entity.Status is not (CompanyPersonalizedDocumentStatus.Historico or CompanyPersonalizedDocumentStatus.Activo))
                {
                    // pendiente | rechazado — nunca se reactivan (409 version_no_activable).
                    return new ReactivateCompanyPersonalizedDocumentResult(
                        PersonalizedDocumentReactivationOutcome.InvalidStatus, null);
                }

                if (entity.IsActive)
                {
                    // Reactivar la ya activa: no-op idempotente (AC — repetible en cualquier orden).
                    return new ReactivateCompanyPersonalizedDocumentResult(
                        PersonalizedDocumentReactivationOutcome.Reactivated, entity.Version);
                }

                var now = DateTimeOffset.UtcNow;

                // Retira (a histórico) la activa previa del mismo tipo — nunca se borra (restricción 9).
                var previousActive = await _context.CompanyPersonalizedDocuments
                    .Where(d => d.TenantId == tenantId
                        && d.DocumentType == entity.DocumentType
                        && d.IsActive
                        && d.Id != id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                foreach (var previous in previousActive)
                {
                    previous.IsActive = false;
                    previous.Status = CompanyPersonalizedDocumentStatus.Historico;
                    previous.DeactivatedAt = now;
                    previous.DeactivatedBy = reactivatedBy;
                    previous.UpdatedAt = now;
                    previous.UpdatedBy = reactivatedBy;
                }

                entity.Status = CompanyPersonalizedDocumentStatus.Activo;
                entity.IsActive = true;
                entity.ActivatedAt = now;
                entity.ActivatedBy = reactivatedBy;
                entity.DeactivatedAt = null;
                entity.DeactivatedBy = null;
                entity.UpdatedAt = now;
                entity.UpdatedBy = reactivatedBy;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new ReactivateCompanyPersonalizedDocumentResult(
                    PersonalizedDocumentReactivationOutcome.Reactivated, entity.Version);
            },
            cancellationToken);

    public Task<CompanyPersonalizedDocumentRecord?> GetActiveAsync(
        Guid tenantId, string documentType, CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyPersonalizedDocuments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        d => d.TenantId == tenantId && d.DocumentType == documentType && d.IsActive,
                        cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : Map(entity);
            },
            cancellationToken);

    public Task<bool> DeactivateActiveAsync(
        Guid tenantId,
        string documentType,
        Guid? deactivatedBy,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var actives = await _context.CompanyPersonalizedDocuments
                    .Where(d => d.TenantId == tenantId && d.DocumentType == documentType && d.IsActive)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (actives.Count == 0)
                {
                    // Ya no había ninguna versión activa — idempotente (AC: "volver al sistema" repetible).
                    return false;
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var active in actives)
                {
                    active.IsActive = false;
                    active.Status = CompanyPersonalizedDocumentStatus.Historico;
                    active.DeactivatedAt = now;
                    active.DeactivatedBy = deactivatedBy;
                    active.UpdatedAt = now;
                    active.UpdatedBy = deactivatedBy;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    private static CompanyPersonalizedDocumentRecord Map(CompanyPersonalizedDocumentEntity e) => new(
        e.Id,
        e.TenantId,
        e.DocumentType,
        e.Version,
        e.Status,
        e.IsActive,
        e.Filename,
        e.StoragePath,
        e.StorageSha256,
        e.SizeBytes,
        e.PageCount,
        e.Notes,
        e.CreatedAt,
        e.CreatedBy,
        e.ActivatedAt,
        e.ActivatedBy,
        e.DeactivatedAt,
        e.DeactivatedBy);
}
