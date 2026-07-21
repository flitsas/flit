namespace Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;

/// <summary>
/// Cuerpo del alta de una firma del baúl (POST). El artefacto de firma viaja como PNG en
/// <c>ArtefactoFirmaBase64</c> (base64, con o sin prefijo <c>data:image/png;base64,</c>) — contrato
/// JSON simple, coherente con el resto del admin. El material NO se persiste en BD: se sube a S3 vía
/// el puerto de storage y en la fila queda solo el path + hash (ADR-0025 §3). <c>DocumentNumber</c>
/// es PII (Ley 1581).
/// </summary>
public sealed record CreateSignatureVaultRequest(
    string? DocumentType,
    string? DocumentNumber,
    string? NitEmpresa,
    string? FullName,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    string? ArtefactoFirmaBase64,
    Guid? MandateSignerId);
