using System.Security.Cryptography;
using System.Text;
using Flit.Infrastructure.Documents.Improntas;
using Flit.Tramites.Application.Documents;

namespace Flit.Infrastructure.Documents.Improntas;

/// <summary>
/// Verificación RSA-SHA256 PKCS#1 de firma de impronta manual (paridad legacy BackCrudTransfer).
/// </summary>
public sealed class ImprontaManualSignatureVerifier : IImprontaManualSignatureVerifier
{
    public bool Verify(string publicKeyPem, string documentHash, string signatureBase64) =>
        ImprontaManualStamper.VerifyFirmaDigital(publicKeyPem, documentHash, signatureBase64);
}
