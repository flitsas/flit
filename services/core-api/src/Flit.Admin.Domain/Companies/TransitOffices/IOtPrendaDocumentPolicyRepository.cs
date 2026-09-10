namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>Fila dispersa: check activo ⇒ documento de prenda opcional para el par compañía+OT.</summary>
public sealed record OtPrendaDocumentPolicyItem(Guid TransitOfficeId, bool DocumentOptional);

/// <summary>
/// Repositorio de políticas de documento de prenda (opt-out) por compañía + OT.
/// </summary>
public interface IOtPrendaDocumentPolicyRepository
{
    Task<IReadOnlyList<OtPrendaDocumentPolicyItem>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// <c>true</c> si hay opt-out vigente (document_optional) con fecha efectiva ≤ snapshot.
    /// </summary>
    Task<bool> IsDocumentOptionalAtAsync(
        Guid tenantId,
        Guid transitOfficeId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fija el estado deseado. <paramref name="documentOptional"/> false elimina la fila (default = obligatorio).
    /// </summary>
    Task SetAsync(
        Guid tenantId,
        Guid transitOfficeId,
        bool documentOptional,
        Guid? changedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Compañías con grant al OT y su flag de opt-out (para hub OT).</summary>
    Task<IReadOnlyList<OtPrendaDocumentPolicyCompanyItem>> ListCompaniesForOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}

public sealed record OtPrendaDocumentPolicyCompanyItem(
    Guid TenantId,
    string TenantName,
    bool DocumentOptional);
