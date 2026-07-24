using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Persistencia EF Core del bloque de validación de identidad administrativa (HU #10907, ADR-0034).
/// Tenant-scoped: cada operación fija <c>app.current_tenant_id</c> (<see cref="TenantRlsScope"/>), igual
/// que el baúl de firmas y el directorio de representantes. Mapea entre el agregado
/// <see cref="AdminIdentityValidation"/> (invariantes) y la entidad de persistencia.
/// <c>DocumentNumber</c>/<c>Email</c> son PII (Ley 1581): no loguear.
/// </summary>
internal sealed class AdminIdentityValidationRepository : IAdminIdentityValidationRepository
{
    private readonly FlitDbContext _context;

    public AdminIdentityValidationRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task AddAsync(AdminIdentityValidation validation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return TenantRlsScope.ExecuteAsync(
            _context,
            validation.TenantId,
            async () =>
            {
                _context.AdminIdentityValidations.Add(ToEntity(validation));
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public Task UpdateAsync(AdminIdentityValidation validation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validation);

        return TenantRlsScope.ExecuteAsync(
            _context,
            validation.TenantId,
            async () =>
            {
                var entity = await _context.AdminIdentityValidations
                    .FirstOrDefaultAsync(
                        v => v.TenantId == validation.TenantId && v.Id == validation.Id, cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("La validación de identidad a actualizar no existe en el tenant.");

                entity.Status = validation.Status;
                entity.CaptureUrl = validation.CaptureUrl;
                entity.KyverumVerificationId = validation.KyverumVerificationId;
                entity.WebhookSecretEncrypted = validation.WebhookSecretEncrypted;
                entity.ProviderStatus = validation.ProviderStatus;
                entity.ProviderPayload = validation.ProviderPayload;
                entity.CertificateHash = validation.CertificateHash;
                entity.ValidatedAt = validation.ValidatedAt;
                entity.ValidUntil = validation.ValidUntil;
                entity.UpdatedAt = validation.UpdatedAt ?? DateTimeOffset.UtcNow;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    public Task<AdminIdentityValidation?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.AdminIdentityValidations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.Id == id, cancellationToken)
                    .ConfigureAwait(false);
                return entity is null ? null : ToAggregate(entity);
            },
            cancellationToken);

    public Task<AdminIdentityValidation?> FindLatestBySubjectAsync(
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        var type = subjectType.Trim();

        return TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.AdminIdentityValidations
                    .AsNoTracking()
                    .Where(v => v.TenantId == tenantId && v.SubjectType == type && v.SubjectRef == subjectRef)
                    .OrderByDescending(v => v.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                return entity is null ? null : ToAggregate(entity);
            },
            cancellationToken);
    }

    private static AdminIdentityValidationEntity ToEntity(AdminIdentityValidation v) => new()
    {
        Id = v.Id,
        TenantId = v.TenantId,
        SubjectType = v.SubjectType,
        SubjectRef = v.SubjectRef,
        DocumentType = v.DocumentType,
        DocumentNumber = v.DocumentNumber,
        Name = v.Name,
        Email = v.Email,
        Status = v.Status,
        Provider = v.Provider,
        CaptureUrl = v.CaptureUrl,
        KyverumVerificationId = v.KyverumVerificationId,
        WebhookSecretEncrypted = v.WebhookSecretEncrypted,
        ProviderStatus = v.ProviderStatus,
        ProviderPayload = v.ProviderPayload,
        CertificateHash = v.CertificateHash,
        ValidatedAt = v.ValidatedAt,
        ValidUntil = v.ValidUntil,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt,
    };

    private static AdminIdentityValidation ToAggregate(AdminIdentityValidationEntity e) =>
        AdminIdentityValidation.Rehydrate(
            e.Id, e.TenantId, e.SubjectType, e.SubjectRef, e.DocumentType, e.DocumentNumber, e.Name,
            e.Email, e.Status, e.Provider, e.CaptureUrl, e.KyverumVerificationId, e.WebhookSecretEncrypted,
            e.ProviderStatus, e.ProviderPayload, e.CertificateHash, e.ValidatedAt, e.ValidUntil,
            e.CreatedAt, e.UpdatedAt);
}
