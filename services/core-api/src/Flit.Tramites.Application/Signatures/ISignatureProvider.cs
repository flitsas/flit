namespace Flit.Tramites.Application.Signatures;

/// <summary>Contexto para crear un sobre de firma: instancia + parte + documento.</summary>
/// <param name="ProcedureInstanceId">Instancia del trámite.</param>
/// <param name="Parte">comprador | vendedor.</param>
/// <param name="DocTipo">Tipo de documento a firmar (compraventa).</param>
/// <param name="Nombre">Nombre legible del firmante (puede venir del actor).</param>
public sealed record SignatureRequest(
    Guid ProcedureInstanceId,
    string Parte,
    string DocTipo,
    string? Nombre);

/// <summary>Resultado de crear el sobre: id del sobre + URL para firmar.</summary>
public sealed record SignatureEnvelope(string EnvelopeId, string SignUrl);

/// <summary>Resultado de completar (firmar) un sobre: ruta del PDF firmado + su SHA-256.</summary>
public sealed record SignatureCompletion(string PdfPath, string Sha256);

/// <summary>
/// Contrato del proveedor de firma electrónica. La implementación actual es un MOCK
/// (<see cref="MockSignatureProvider"/>); reemplazable por uno real (ZapSign) sin tocar los
/// handlers — mismo patrón contract-first que el scorer biométrico y los consultation providers.
/// </summary>
public interface ISignatureProvider
{
    /// <summary>Crea el sobre de firma para una parte y devuelve envelopeId + signUrl.</summary>
    SignatureEnvelope CreateEnvelope(SignatureRequest request);
}
