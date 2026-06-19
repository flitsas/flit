namespace Flit.Tramites.Application.Signatures;

/// <summary>
/// MOCK: proveedor de firma electrónica determinista para el Slice 7. No hace IO ni llama a ningún
/// servicio externo. <c>CreateEnvelope</c> devuelve un envelopeId sintético y una signUrl local.
///
/// TODO(slice-firma-real): reemplazar por un proveedor real (ZapSign) que cree el sobre vía API y
/// devuelva la URL real de firma. Implementar ISignatureProvider y cambiar el registro en DI — los
/// handlers no cambian.
/// </summary>
public sealed class MockSignatureProvider : ISignatureProvider
{
    public SignatureEnvelope CreateEnvelope(SignatureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // MOCK: envelopeId determinista por (instancia, parte, doc) + signUrl local hacia el portal.
        var envelopeId = $"mock-env-{request.ProcedureInstanceId:N}-{request.Parte}-{request.DocTipo}";
        var signUrl = $"/sign/{envelopeId}";
        return new SignatureEnvelope(envelopeId, signUrl);
    }
}
