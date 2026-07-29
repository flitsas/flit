using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador del puerto <see cref="IAdminIdentitySubjectLinker"/> (HU #10907, ADR-0034): vincula una
/// validación de identidad administrativa APROBADA a su sujeto despachando por <c>subjectType</c>. Para
/// <c>legal_representative</c> setea <c>identity_validation_ref</c> del representante usando el agregado
/// (<see cref="LegalRepresentative.LinkIdentity"/>), que preserva la exclusión firma/identidad. Un nuevo
/// sujeto (p.ej. mandatario) solo añade una rama aquí, sin tocar el servicio. Tenant-scoped (RLS).
/// </summary>
internal sealed class AdminIdentitySubjectLinker : IAdminIdentitySubjectLinker
{
    private readonly FlitDbContext _context;

    public AdminIdentitySubjectLinker(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<bool> LinkAsync(
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        Guid validationRef,
        Guid? actorBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);

        return subjectType.Trim() switch
        {
            AdminIdentitySubjectTypes.LegalRepresentative =>
                LinkLegalRepresentativeAsync(tenantId, subjectRef, validationRef, actorBy, cancellationToken),
            AdminIdentitySubjectTypes.MandateSigner =>
                LinkMandateSignerAsync(subjectRef, validationRef, actorBy, cancellationToken),
            _ => Task.FromResult(false),
        };
    }

    /// <summary>
    /// Ancla la identidad aprobada al mandatario (HU #11000): setea <c>identity_validation_ref</c> en
    /// <c>admin.mandate_signers</c>. La tabla NO tiene RLS (se acota por organismo de tránsito, no por
    /// tenant), así que la escritura es directa y no necesita <see cref="TenantRlsScope"/> — el control de
    /// acceso lo hace el endpoint (SuperAdmin / ot_admin). Idempotente: si ya apunta a la misma validación
    /// no reescribe. La validación viene del bloque agnóstico ya anclado al tenant del OT, así que el id
    /// del mandatario basta como llave.
    /// </summary>
    private async Task<bool> LinkMandateSignerAsync(
        Guid mandateSignerId,
        Guid validationRef,
        Guid? actorBy,
        CancellationToken cancellationToken)
    {
        var entity = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == mandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        if (entity.IdentityValidationRef == validationRef)
        {
            return true;
        }

        entity.IdentityValidationRef = validationRef;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = actorBy;
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private Task<bool> LinkLegalRepresentativeAsync(
        Guid tenantId,
        Guid representativeId,
        Guid validationRef,
        Guid? actorBy,
        CancellationToken cancellationToken) =>
        TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.CompanyLegalRepresentatives
                    .FirstOrDefaultAsync(
                        r => r.TenantId == tenantId && r.Id == representativeId, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return false;
                }

                // Rehidrata + aplica la invariante del agregado (identidad excluye firma del baúl).
                var aggregate = LegalRepresentative.Rehydrate(
                    entity.Id, entity.TenantId, entity.RepresentedCompanyId, entity.DocumentType,
                    entity.DocumentNumber, entity.FirstLastName, entity.SecondLastName, entity.Name,
                    entity.Email, entity.Address, entity.City, entity.Phone, entity.SignatureVaultId,
                    entity.IdentityValidationRef, entity.IsActive);
                aggregate.LinkIdentity(validationRef);

                // Idempotente: si ya estaba vinculada a la misma validación, no reescribe.
                if (entity.IdentityValidationRef == aggregate.IdentityValidationRef
                    && entity.SignatureVaultId == aggregate.SignatureVaultId)
                {
                    return true;
                }

                entity.IdentityValidationRef = aggregate.IdentityValidationRef;
                entity.SignatureVaultId = aggregate.SignatureVaultId;
                entity.UpdatedAt = DateTimeOffset.UtcNow;
                entity.UpdatedBy = actorBy;
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
}
