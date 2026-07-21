namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Firma del baúl vigente resuelta para el consumo en un trámite (ADR-0025 §4). NO expone el
/// material criptográfico: solo la referencia al artefacto en storage (<see cref="StoragePath"/> +
/// <see cref="StorageSha256"/>), la huella de integridad y el nombre del firmante. Esta es la
/// costura que consume la rama <c>firma_baul</c> de <c>EnsureIdentity</c> (HU #10645) y el
/// productor de <c>FurDocumentData.FirmaImagenes</c>.
/// </summary>
public sealed record SignatureVaultMatch(
    Guid SignatureVaultId,
    string FullName,
    string SignatureHash,
    string StoragePath,
    string StorageSha256,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    string DocumentNumber);

/// <summary>
/// Puerto para resolver la firma precargada del baúl aplicable a un actor jurídico (por NIT),
/// según la configuración del tenant (<c>admin.tenant_operational_policies.signature_vault_enabled</c>,
/// HU #10642/#10643). Desacopla el módulo de trámites del de Admin (mismo patrón que
/// <see cref="IIdentityValidationPolicy"/> / <see cref="IRnmcRequirementPolicy"/>). Este puerto
/// ACTIVA el flag <c>SignatureVaultEnabled</c>, hasta ahora inerte (ADR-0025 §4).
/// </summary>
public interface ISignatureVaultPolicy
{
    /// <summary>
    /// Resuelve la firma del baúl <b>activa y vigente</b> para <paramref name="nitEmpresa"/> dentro
    /// del tenant, SIEMPRE que el baúl esté habilitado para el tenant. Devuelve <c>null</c> cuando el
    /// baúl está deshabilitado, no hay firma activa, o la firma no está vigente hoy (hora Colombia
    /// UTC-5). El material de firma NUNCA se devuelve: solo la referencia al artefacto.
    /// </summary>
    Task<SignatureVaultMatch?> ResolveAsync(
        Guid tenantId,
        string nitEmpresa,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación segura que NUNCA resuelve una firma de baúl (devuelve <c>null</c>) — default para
/// tests de dominio/aplicación que no ejercitan el baúl. Con esta política, el flujo de identidad se
/// comporta como si el baúl estuviera deshabilitado.
/// </summary>
public sealed class NullSignatureVaultPolicy : ISignatureVaultPolicy
{
    public static NullSignatureVaultPolicy Instance { get; } = new();

    public Task<SignatureVaultMatch?> ResolveAsync(
        Guid tenantId,
        string nitEmpresa,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SignatureVaultMatch?>(null);
}
