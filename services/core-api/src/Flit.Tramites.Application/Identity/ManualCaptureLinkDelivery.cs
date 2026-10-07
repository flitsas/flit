using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Entrega del enlace de captura manual al titular (HU #13287, Épica #13202), común a la activación y a la regeneración. Entrega el
/// token en claro UNA vez al puerto <see cref="IManualCaptureLinkNotifier"/> y, si el correo no sale (titular sin correo o fallo
/// del proveedor), deja la etapa de auditoría <c>manual_correo_fallido</c> SIN PII ni token ni correo. Nunca lanza por el envío:
/// la activación/regeneración ya está confirmada y no se revierte.
/// </summary>
internal static class ManualCaptureLinkDelivery
{
    public static async Task<bool> SendAsync(
        IManualCaptureLinkNotifier notifier,
        IIdentityValidationAuditLog audit,
        ProcedureInstanceBiometricValidation v,
        string token,
        CancellationToken ct,
        string? rejectionReasonLabel = null)
    {
        var enviado = false;
        var causa = "sin_correo";

        if (!string.IsNullOrWhiteSpace(v.Email))
        {
            causa = "envio_fallido";
            try
            {
                enviado = await notifier
                    .NotifyAsync(new ManualCaptureLink(v.Id, v.TenantId, token, v.ExpiresAt, v.Email, v.Name, rejectionReasonLabel), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                enviado = false;
                causa = "envio_error";
            }
        }

        if (!enviado)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.ManualCorreoFallido, IdentityValidationAuditOutcomes.Error,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                PartyRole: v.PartyRole,
                Message: "El correo con el enlace de captura manual no salió; el Super Admin puede regenerar el enlace.",
                Detail: $"causa={causa}"), ct).ConfigureAwait(false);
        }

        return enviado;
    }
}
