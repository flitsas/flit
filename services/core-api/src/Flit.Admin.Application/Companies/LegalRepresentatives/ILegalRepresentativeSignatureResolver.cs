namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Resuelve, al guardar un representante legal (HU #10900, ADR-0033 §5.1, D2), su firma del baúl o su
/// validación de identidad vigente. Precedencia (coherente con <c>IdentityApprovalResolver</c>):
/// (1) firma del baúl activa+vigente por NIT → (2) validación de identidad vigente por documento →
/// (3) ninguna. La capa Application NO depende de <c>Tramites.Domain</c>: la consulta de identidad se
/// abstrae en <see cref="IRepresentativeIdentityLookup"/>.
/// </summary>
public interface ILegalRepresentativeSignatureResolver
{
    /// <summary>
    /// Resuelve el vínculo de firma/identidad para <paramref name="documentoRepresentante"/> de la
    /// compañía <paramref name="nitCompania"/> en la fecha <paramref name="today"/>.
    /// </summary>
    Task<LegalRepresentativeSignatureResolution> ResolveAsync(
        Guid tenantId,
        string nitCompania,
        string tipoDocumento,
        string documentoRepresentante,
        DateOnly today,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resultado del resolutor. Firma e identidad son EXCLUYENTES (a lo sumo una no nula).
/// <see cref="HasSignatureOrIdentity"/> es <c>false</c> cuando no hay ninguna vigente: la señal para
/// que el FE ofrezca "enviar correo de validación" o "registrar en baúl".
/// </summary>
public sealed record LegalRepresentativeSignatureResolution(
    Guid? SignatureVaultId,
    Guid? IdentityValidationRef,
    bool HasSignatureOrIdentity)
{
    /// <summary>Ninguna firma ni identidad vigente.</summary>
    public static LegalRepresentativeSignatureResolution None { get; } = new(null, null, false);

    /// <summary>Vínculo a una firma del baúl vigente.</summary>
    public static LegalRepresentativeSignatureResolution FromSignature(Guid signatureVaultId) =>
        new(signatureVaultId, null, true);

    /// <summary>Vínculo a una validación de identidad vigente.</summary>
    public static LegalRepresentativeSignatureResolution FromIdentity(Guid identityValidationRef) =>
        new(null, identityValidationRef, true);
}
