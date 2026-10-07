using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Épica #13202 — el flujo manual de identidad es una herramienta interna del Super Admin FLIT. Para la compañía y el
/// cliente la bitácora de una validación manual se ve como la de una validación biométrica normal: los eventos del flujo
/// manual se traducen a su equivalente normal (envío, notificación recibida, resultado aplicado, reenvío), sin mensajes
/// ni usuarios, y los que no tienen equivalente (consentimiento, consulta de imágenes, correo fallido, cancelaciones
/// internas) no se muestran. Los eventos que no son del flujo manual pasan intactos.
/// </summary>
public static class IdentityAuditParaCliente
{
    public static IReadOnlyList<IdentityAuditEventDto> Aplicar(IReadOnlyList<IdentityAuditEventDto> events)
    {
        var salida = new List<IdentityAuditEventDto>(events.Count);
        foreach (var e in events)
        {
            if (!EsManual(e.Stage))
            {
                salida.Add(e);
                continue;
            }
            var equivalente = Equivalente(e);
            if (equivalente is not null) salida.Add(equivalente);
        }
        return salida;
    }

    public static bool EsManual(string stage) => stage.Contains("manual", StringComparison.OrdinalIgnoreCase);

    private static IdentityAuditEventDto? Equivalente(IdentityAuditEventDto e)
    {
        var (stage, outcome) = e.Stage switch
        {
            IdentityValidationAuditStages.ManualActivado => (IdentityValidationAuditStages.Send, IdentityValidationAuditOutcomes.Ok),
            IdentityValidationAuditStages.ManualEnlaceRegenerado => (IdentityValidationAuditStages.Resend, IdentityValidationAuditOutcomes.Ok),
            IdentityValidationAuditStages.ManualCapturaRecibida => (IdentityValidationAuditStages.WebhookReceived, IdentityValidationAuditOutcomes.Received),
            IdentityValidationAuditStages.ManualAprobado => (IdentityValidationAuditStages.WebhookApplied, IdentityValidationAuditOutcomes.Approved),
            IdentityValidationAuditStages.ManualRechazado => (IdentityValidationAuditStages.WebhookApplied, IdentityValidationAuditOutcomes.Rejected),
            _ => (null, null),
        };
        if (stage is null || outcome is null) return null;
        return new IdentityAuditEventDto(
            e.OccurredAt, stage, outcome, HttpStatus: null, SignaturePresent: null, SecretPresent: null,
            DecryptOk: null, ProviderStatus: null, ErrorType: null, Message: null);
    }
}
