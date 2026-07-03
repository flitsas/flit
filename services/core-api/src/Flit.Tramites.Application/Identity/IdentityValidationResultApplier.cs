using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Resultado terminal de una validación de identidad, agnóstico de la fuente (webhook o consulta/poll).
/// <paramref name="ProviderStatus"/> y <paramref name="SanitizedPayload"/> ya vienen sanitizados (sin PII/OCR).
/// </summary>
public sealed record IdentityValidationTerminalResult(
    bool Approved, string? ProviderStatus, string SanitizedPayload, int? Score, string? CertificateHash = null);

/// <summary>
/// Punto ÚNICO donde un resultado terminal (aprobado|rechazado) se aplica a una validación de identidad:
/// setea el estado (vía <see cref="ProcedureInstanceBiometricValidation.Approve"/> o rechazo), estampa
/// provider_status/payload/score y emite el evento <see cref="IdentityValidationCompleted"/> (outbox).
/// Idempotente: si la validación ya está en estado terminal no hace nada. NO persiste — el caller decide
/// cuándo hacer <c>SaveChanges</c>. Lo usan por igual el webhook y la reconciliación por consulta, para que
/// ambos caminos apliquen EXACTAMENTE la misma lógica (aprobación + vigencia + evento).
/// </summary>
public sealed class IdentityValidationResultApplier(IIdentityValidationEventPublisher events)
{
    public async Task<bool> ApplyAsync(
        ProcedureInstanceBiometricValidation v,
        IdentityValidationTerminalResult result,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(result);

        // Idempotencia: los estados terminales no se re-aplican ni re-emiten evento.
        if (v.Status is BiometricEstados.Aprobado or BiometricEstados.Rechazado)
            return false;

        if (result.Approved)
        {
            v.Approve(now); // estado + validated_at + estampa valid_until + updated_at
            // Estampa la serie/hash del certificado (firmaSerie) al aprobar; no la sobreescribe con null si el
            // origen no la trae. Gana quien alcanza el estado terminal primero: normalmente el webhook (que sí
            // trae firmaSerie); si aprueba antes la reconciliación por GET (webhook perdido) y Kyverum no expone
            // firmaSerie ahí, el hash queda null y el sello del FUR muestra la serie ausente — degradación
            // acotada al caso "el webhook nunca llegó".
            if (!string.IsNullOrWhiteSpace(result.CertificateHash))
                v.CertificateHash = result.CertificateHash;
        }
        else
        {
            v.Status = BiometricEstados.Rechazado;
            v.UpdatedAt = now;
        }

        v.ProviderStatus = result.ProviderStatus;
        v.ProviderPayload = result.SanitizedPayload;
        v.Score = result.Score;

        await events.PublishAsync(new IdentityValidationCompleted
        {
            TenantId = v.TenantId,
            ProcedureInstanceId = v.ProcedureInstanceId,
            ValidationId = v.Id,
            Provider = v.Provider,
            Parte = v.PartyRole,
            Estado = v.Status,
            ProviderStatus = result.ProviderStatus,
            Score = result.Score,
        }, ct);

        return true;
    }
}
