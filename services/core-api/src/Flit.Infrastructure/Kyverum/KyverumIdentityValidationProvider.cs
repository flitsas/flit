using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;

namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// Adaptador de Kyverum a la abstracción provider-agnostic <see cref="IIdentityValidationProvider"/>:
/// envuelve <see cref="IKyverumVerifyClient"/> y traduce <see cref="KyverumVerifyException"/> a
/// <see cref="IdentityProviderException"/> (conservando transitorio/definitivo). Así la cola de envío y su
/// reintento no dependen de Kyverum: otro proveedor solo implementa esta misma interfaz.
/// </summary>
internal sealed class KyverumIdentityValidationProvider(IKyverumVerifyClient kyverum)
    : IIdentityValidationProvider
{
    public string Name => BiometricProviders.Kyverum;

    public async Task<IdentityProviderStartResult> StartAsync(
        IdentityProviderStartRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var r = await kyverum.StartVerificationAsync(
                new KyverumVerifyStartRequest(
                    request.ProcedureInstanceId,
                    request.ValidationId,
                    request.Parte,
                    request.Nombre,
                    request.TipoDoc,
                    request.Documento,
                    request.Email),
                ct);

            return new IdentityProviderStartResult(
                r.VerificationId, r.CaptureUrl, r.WebhookSecret, r.ProviderStatus, r.RawPayloadSanitized);
        }
        catch (KyverumVerifyException ex)
        {
            throw new IdentityProviderException(ex.Message, ex.Transient);
        }
    }
}
