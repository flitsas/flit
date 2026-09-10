namespace Flit.Tramites.Application.Documents;

/// <summary>
/// Verifica la firma RSA-SHA256 PKCS#1 de una impronta manual estampada (HU #12148).
/// Nunca usa la clave privada.
/// </summary>
public interface IImprontaManualSignatureVerifier
{
    /// <summary>
    /// Comprueba que <paramref name="signatureBase64"/> firma
    /// <paramref name="documentHash"/> (UTF-8) con <paramref name="publicKeyPem"/>.
    /// </summary>
    bool Verify(string publicKeyPem, string documentHash, string signatureBase64);
}
