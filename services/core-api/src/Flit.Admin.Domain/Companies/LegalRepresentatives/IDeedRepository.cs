namespace Flit.Admin.Domain.Companies.LegalRepresentatives;

/// <summary>
/// Escrituras de escrituras (HU #10900, ADR-0033). Tenant-scoped: cada operación fija
/// <c>app.current_tenant_id</c> (RLS), igual que el baúl de firmas.
/// </summary>
public interface IDeedRepository
{
    /// <summary>Alta de una escritura + su puente de compañías. Devuelve el id generado.</summary>
    Task<Guid> CreateAsync(SaveDeedData data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Edición de una escritura (descripción, vigencia, artefacto opcional, compañías). <c>false</c>
    /// si no existe en el tenant.
    /// </summary>
    Task<bool> UpdateAsync(SaveDeedData data, CancellationToken cancellationToken = default);

    /// <summary>Baja lógica de la escritura. <c>false</c> si no existe o ya estaba inactiva.</summary>
    Task<bool> DeactivateAsync(
        Guid tenantId,
        Guid id,
        Guid? changedBy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Datos de alta/edición de una escritura. <c>Id</c> nulo = alta. En edición, <c>StoragePath</c>/
/// <c>StorageSha256</c> nulos conservan el artefacto actual. <c>RepresentativeId</c> (Feature #10929)
/// solo aplica en el ALTA (el representante que la asocia); en la edición se conserva el actual.
/// </summary>
public sealed record SaveDeedData(
    Guid TenantId,
    Guid? Id,
    string Description,
    string? StoragePath,
    string? StorageSha256,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    IReadOnlyList<Guid> RepresentedCompanyIds,
    Guid? ActorBy,
    Guid? RepresentativeId = null);
