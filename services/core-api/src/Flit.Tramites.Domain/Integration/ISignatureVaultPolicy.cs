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
    string DocumentNumber,
    // HU #10930 (Feature #10929): código alfanumérico que el usuario digitó al custodiar la firma en el
    // baúl. Es lo que se estampa como "Hash" en el FUR (NO el UUID de la fila). null si la firma no lo trae.
    string? CodigoHash = null);

/// <summary>
/// Puerto para resolver la firma precargada del baúl aplicable a un actor jurídico, según la
/// configuración del tenant (<c>admin.tenant_operational_policies.signature_vault_enabled</c>,
/// HU #10642/#10643). Desacopla el módulo de trámites del de Admin (mismo patrón que
/// <see cref="IIdentityValidationPolicy"/> / <see cref="IRnmcRequirementPolicy"/>). Este puerto
/// ACTIVA el flag <c>SignatureVaultEnabled</c>, hasta ahora inerte (ADR-0025 §4).
/// <para><b>HU #10930/#10937:</b> el baúl dejó de llavearse por NIT de la compañía y ahora es de la
/// PERSONA (documento). Para un actor jurídico, la firma se resuelve por el documento del
/// REPRESENTANTE LEGAL seleccionado (el sujeto de identidad del actor), no por el NIT — así, cuando
/// una compañía tiene varios representantes, firma el elegido con su propia firma del baúl.</para>
/// </summary>
public interface ISignatureVaultPolicy
{
    /// <summary>
    /// Resuelve la firma del baúl <b>activa y vigente</b> de la persona
    /// (<paramref name="documentType"/> + <paramref name="documentNumber"/> — el representante legal
    /// seleccionado del actor jurídico) dentro del tenant, SIEMPRE que el baúl esté habilitado para el
    /// tenant. Devuelve <c>null</c> cuando el baúl está deshabilitado, no hay firma activa de esa
    /// persona, o la firma no está vigente hoy (hora Colombia UTC-5). El material de firma NUNCA se
    /// devuelve: solo la referencia al artefacto. <paramref name="documentNumber"/> es PII
    /// (Ley 1581): no loguear.
    /// </summary>
    Task<SignatureVaultMatch?> ResolveAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
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
        string documentType,
        string documentNumber,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SignatureVaultMatch?>(null);
}
