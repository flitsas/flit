using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.Identity.Rounds;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>
/// Implementa <see cref="IIdentityRejectionReissue"/> con el mismo núcleo de reenvío (<see cref="PrevalidacionResendService"/>):
/// el tope de rondas se valida ahí, así que sin rondas no se reemite nada. Solo prevalidaciones sueltas de Kyverum; las de
/// trámite siguen su propio flujo.
/// </summary>
internal sealed class IdentityRejectionReissueService(
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events,
    IIdentityRoundsPolicy? rounds = null) : IIdentityRejectionReissue
{
    private readonly PrevalidacionResendService _resend = new(kyverum, providerOptions, secretProtector, events, rounds);

    public async Task TryReissueAsync(ProcedureInstanceBiometricValidation validation, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        if (validation.ProcedureInstanceId is not null)
            return;
        if (!string.Equals(validation.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(validation.KyverumVerificationId))
            return;

        try
        {
            var subject = new IdentitySubjectStandalone(validation.Name, validation.DocumentType, validation.DocumentNumber, validation.Email);
            // El error (p. ej. limite_rondas_alcanzado) no es una falla: significa que la persona ya no tiene rondas.
            await _resend.ResendAsync(validation.TenantId, validation, subject, now, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // El rechazo ya quedó aplicado; una falla de la reemisión no lo revierte. Se reintenta en el siguiente sondeo.
        }
    }
}
