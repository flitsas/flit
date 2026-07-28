namespace Flit.Admin.Domain.Companies.SignatureVault;

/// <summary>
/// Lecturas del baúl de firmas (ADR-0025). El baúl es tenant-scoped: las lecturas corren bajo el
/// contexto RLS del tenant (<c>app.current_tenant_id</c>). La firma vigente para el consumo se
/// resuelve por (tenant, NIT); la vigencia efectiva la decide el agregado con
/// <see cref="SignatureVault.EstaVigente"/>. <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
public interface ISignatureVaultReader
{
    /// <summary>
    /// Firma 'activa' del baúl para una compañía (por NIT) dentro del tenant. Base del consumo
    /// (ADR-0025 §4). Devuelve el agregado rehidratado para que el llamador aplique
    /// <see cref="SignatureVault.EstaVigente"/>; <c>null</c> si no hay ninguna activa.
    /// </summary>
    Task<SignatureVault?> FindActiveByNitAsync(
        Guid tenantId,
        string nitEmpresa,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Firma 'activa' del baúl para una PERSONA (por tipo + número de documento) dentro del tenant
    /// (HU #10930, Feature #10929): la firma es de la persona + tenant, ya no depende del NIT.
    /// Devuelve la más reciente rehidratada para que el llamador aplique
    /// <see cref="SignatureVault.EstaVigente"/>; <c>null</c> si no hay ninguna activa.
    /// <c>documentNumber</c> es PII (Ley 1581): no loguear.
    /// </summary>
    Task<SignatureVault?> FindActiveByDocumentAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Firmas del baúl del tenant (activas y revocadas) para la gestión admin, ordenadas primero
    /// las activas y luego por creación descendente.
    /// </summary>
    Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Una firma por id dentro del tenant (cualquier estado). <c>null</c> si no existe. Usada para
    /// precargar el detalle / formulario de gestión.
    /// </summary>
    Task<SignatureVaultItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default);
}
