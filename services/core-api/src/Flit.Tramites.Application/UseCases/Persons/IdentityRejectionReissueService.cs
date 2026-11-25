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
    IIdentityValidationAuditLog audit,
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
            // Ronda que acaba de cerrar en rechazo final (antes del reenvío, para que quede en el historial aunque el reenvío falle).
            var rondaCerrada = 1 + Math.Max(0, validation.ResendCount);
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.RoundRejected, IdentityValidationAuditOutcomes.Rejected,
                TenantId: validation.TenantId, ValidationId: validation.Id, KyverumVerificationId: validation.KyverumVerificationId,
                PartyRole: validation.PartyRole, ProviderStatus: validation.ProviderStatus,
                Message: $"Ronda {rondaCerrada} cerrada en rechazo: se agotaron los intentos de la captura.",
                Detail: $"ronda={rondaCerrada}; intentos={validation.Attempts}/{validation.MaxAttempts}"), ct);

            var subject = new IdentitySubjectStandalone(validation.Name, validation.DocumentType, validation.DocumentNumber, validation.Email);
            // El error (p. ej. limite_rondas_alcanzado) no es una falla: significa que la persona ya no tiene rondas.
            var (error, _, _, captureUrl) = await _resend.ResendAsync(validation.TenantId, validation, subject, now, ct);
            if (error is not null)
            {
                await audit.LogAsync(new IdentityValidationAuditEntry(
                    IdentityValidationAuditStages.RoundExhausted, IdentityValidationAuditOutcomes.Rejected,
                    TenantId: validation.TenantId, ValidationId: validation.Id, KyverumVerificationId: validation.KyverumVerificationId,
                    PartyRole: validation.PartyRole, ErrorType: error,
                    Message: "Sin rondas disponibles: el rechazo queda como final.",
                    Detail: $"ronda={rondaCerrada}; motivo={error}"), ct);
            }
            else if (!string.IsNullOrWhiteSpace(captureUrl))
            {
                await audit.LogAsync(new IdentityValidationAuditEntry(
                    IdentityValidationAuditStages.RoundReissued, IdentityValidationAuditOutcomes.Ok,
                    TenantId: validation.TenantId, ValidationId: validation.Id, KyverumVerificationId: validation.KyverumVerificationId,
                    PartyRole: validation.PartyRole,
                    Message: $"Ronda {rondaCerrada + 1} abierta: enlace nuevo emitido tras el rechazo.",
                    Detail: $"ronda={rondaCerrada + 1}"), ct);
            }
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
