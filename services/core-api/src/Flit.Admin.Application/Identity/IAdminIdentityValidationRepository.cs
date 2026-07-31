using Flit.Admin.Domain.Identity;

namespace Flit.Admin.Application.Identity;

/// <summary>
/// Persistencia del bloque de validación de identidad administrativa (HU #10907, ADR-0034). Tenant-
/// scoped (RLS por <c>app.current_tenant_id</c>), igual que el resto del directorio Admin. El agregado
/// <see cref="AdminIdentityValidation"/> lleva las invariantes; el índice de BD garantiza el aislamiento.
/// <c>DocumentNumber</c>/<c>Email</c> son PII: no loguear.
/// </summary>
public interface IAdminIdentityValidationRepository
{
    /// <summary>Persiste una validación nueva (alta).</summary>
    Task AddAsync(AdminIdentityValidation validation, CancellationToken cancellationToken = default);

    /// <summary>Persiste los cambios de una validación rehidratada (aprobación/rechazo/expiración/track).</summary>
    Task UpdateAsync(AdminIdentityValidation validation, CancellationToken cancellationToken = default);

    /// <summary>Una validación por id dentro del tenant. <c>null</c> si no existe.</summary>
    Task<AdminIdentityValidation?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// La validación MÁS RECIENTE del sujeto (<paramref name="subjectType"/> + <paramref name="subjectRef"/>)
    /// en el tenant, sin importar el estado. Base del reenvío (respeta vigencia) y del anclaje al sujeto.
    /// <c>null</c> si el sujeto no tiene ninguna.
    /// </summary>
    Task<AdminIdentityValidation?> FindLatestBySubjectAsync(
        Guid tenantId,
        string subjectType,
        Guid subjectRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// La validación APROBADA más reciente de una PERSONA (documento) en el tenant, sin importar a qué
    /// sujeto esté anclada (HU #11000). Permite apalancar una identidad ya validada al registrar un sujeto
    /// nuevo — p. ej. quien ya validó como representante legal no vuelve a validar como mandatario. La
    /// vigencia la evalúa el llamador con <see cref="AdminIdentityValidation.EsAprobadaVigente"/>.
    /// <c>null</c> si esa persona nunca tuvo una validación aprobada. <c>documentNumber</c> es PII.
    /// </summary>
    Task<AdminIdentityValidation?> FindLatestApprovedByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);
}
