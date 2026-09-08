namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Firmante a estampar en la impronta manual. La rúbrica se pinta como en el FUR
/// (PNG + sidecar baúl/identidad). <see cref="FullName"/> no se imprime en el PDF.
/// </summary>
public sealed record ImprontaManualSigner(
    string FullName,
    string? HashPropietario,
    byte[]? SignatureImage,
    string? ImageSidecarText = null,
    string? SealText = null);

/// <summary>Datos del expediente necesarios para sellar una impronta cargada a mano.</summary>
public sealed record ImprontaManualStampContext(
    string ReferenceNumber,
    string? Placa,
    string? Vin,
    string? NumMotor,
    string? NumChasis,
    DateTimeOffset FechaCargue,
    IReadOnlyList<ImprontaManualSigner> Signers);

/// <summary>
/// Resultado del stamp: PDF sellado + material criptográfico (paridad BackCrudTransfer).
/// <see cref="Applied"/> es false si el PDF ya estaba sellado o estaba vacío.
/// </summary>
public sealed record ImprontaManualStampResult(
    byte[] Pdf,
    bool Applied,
    string DocumentHash = "",
    string SignatureBase64 = "",
    string PrivateKeyPem = "",
    string PublicKeyPem = "",
    DateTimeOffset? SignedAt = null,
    bool WasSignedWithoutOwnerSignature = false);

/// <summary>
/// Estampa hash, firmas de propietario(s) y firma digital visual sobre un PDF de impronta
/// <b>manual</b> (no Kyverum). Idempotente: si el PDF ya contiene el marcador
/// <c>Firma digital impronta:</c> (texto) o la keyword de metadata FLIT, no vuelve a estampar.
/// </summary>
public interface IImprontaManualStamper
{
    /// <summary>Marcador visual de zona 3.</summary>
    public const string Marker = "Firma digital impronta:";

    /// <summary>Keyword PDF (metadata, sin comprimir) para idempotencia fiable.</summary>
    public const string MetadataKeyword = "FLIT_IMPRONTA_STAMP_V1";

    bool AlreadyStamped(byte[] pdf);

    ImprontaManualStampResult Stamp(byte[] pdf, ImprontaManualStampContext context);
}
