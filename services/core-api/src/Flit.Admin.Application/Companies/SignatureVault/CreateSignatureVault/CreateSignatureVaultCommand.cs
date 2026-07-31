namespace Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;

/// <summary>
/// Comando de alta de una firma del baúl. <c>ArtefactoFirmaBase64</c> lleva el PNG capturado (base64);
/// el handler lo valida, lo sube a storage y persiste solo el path + hash. <c>DocumentNumber</c> es
/// PII (Ley 1581): no loguear.
/// </summary>
public sealed class CreateSignatureVaultCommand
{
    public required Guid TenantId { get; init; }
    public string? DocumentType { get; init; }
    public string? DocumentNumber { get; init; }
    public string? NitEmpresa { get; init; }
    public string? FullName { get; init; }
    public DateOnly VigenciaDesde { get; init; }
    public DateOnly VigenciaHasta { get; init; }
    public string? ArtefactoFirmaBase64 { get; init; }

    /// <summary>
    /// Código alfanumérico que digita el usuario (HU #10930), DISTINTO del SHA-256 del artefacto.
    /// </summary>
    public string? CodigoHash { get; init; }

    public Guid? MandateSignerId { get; init; }
    public Guid? CreatedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
