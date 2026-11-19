using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.Identity.Events;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.Persons;

/// <summary>Resultado de reenviar la validación de una parte del portal (HU #14045): la misma fila, con enlace nuevo.</summary>
public sealed record ReenviarValidacionPortalResult(BiometricValidationDto Validation, bool Queued);

/// <summary>
/// HU #14045 (Feature #14023, Épica #14021, ADR-0075) — reenvía el enlace de la validación de identidad de una parte del
/// portal de Persona Natural sin crear otra: reutiliza el núcleo de reenvío de la prevalidación
/// (<see cref="PrevalidacionResendService"/>), así que opera sobre la MISMA fila — enlace nuevo y el anterior deja de ser
/// válido (nuevo token, nueva verificación y nuevo secreto de webhook en el proveedor), sin duplicar cargos. Con un correo
/// nuevo, el enlace sale a ese correo (correo sincronizado). Una aprobada no se reenvía. Los topes del portal (5 reenvíos y
/// 60 s entre uno y otro) los aplica el BFF, que es quien cuenta los reenvíos de su solicitud.
/// Errores: <c>not_found</c>, <c>identidad_aprobada</c>, <c>identidad_manual</c>, <c>proveedor_error</c>,
/// <c>validacion_en_curso</c> (carrera con el índice único).
/// </summary>
public sealed class ReenviarValidacionPortalHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    BiometricsProviderOptions providerOptions,
    IWebhookSecretProtector secretProtector,
    IIdentityValidationEventPublisher events,
    IIdentityValidationAuditLog audit)
{
    private readonly PrevalidacionResendService _resend = new(kyverum, providerOptions, secretProtector, events);

    public async Task<(ReenviarValidacionPortalResult? Result, string? Error)> HandleAsync(
        Guid tenantId,
        Guid validationId,
        string? correoNuevo,
        CancellationToken ct = default)
    {
        var validation = await repo.GetPortalValidationByIdAsync(tenantId, validationId, ct);
        if (validation is null)
            return (null, "not_found");

        if (validation.Status == BiometricEstados.Aprobado)
            return (null, "identidad_aprobada");

        var now = DateTimeOffset.UtcNow;
        var email = string.IsNullOrWhiteSpace(correoNuevo) ? validation.Email : correoNuevo.Trim();
        var subject = new IdentitySubjectStandalone(validation.Name, validation.DocumentType, validation.DocumentNumber, email);

        var (error, queued, _, _) = await _resend.ResendAsync(tenantId, validation, subject, now, ct);
        if (error is not null)
            return (null, error);

        // Correo sincronizado: el enlace nuevo salió a este correo, y la fila lo refleja. RegisteredEmail (el original) no se toca.
        validation.Email = email;

        // Habeas Data — el reenvío deja traza (stage Resend) con el correo destino ENMASCARADO, nunca en claro.
        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.Resend,
            IdentityValidationAuditOutcomes.Ok,
            TenantId: tenantId,
            ValidationId: validation.Id,
            Message: $"Reenvío de la validación del portal a {EditarPrevalidacionHandler.MaskEmail(email)}.",
            Detail: $"correo_destino={EditarPrevalidacionHandler.MaskEmail(email)}; reenvios_acumulados={validation.ResendCount}; encolado={queued}"), ct);

        try
        {
            await repo.SaveChangesAsync(ct);
        }
        catch (Domain.Identity.IdentityInFlightConflictException)
        {
            return (null, "validacion_en_curso");
        }

        return (new ReenviarValidacionPortalResult(IniciarBiometriaHandler.ToDto(validation, now), queued), null);
    }
}
