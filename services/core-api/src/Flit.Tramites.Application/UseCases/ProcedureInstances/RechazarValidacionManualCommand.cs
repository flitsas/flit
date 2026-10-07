using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de rechazar una validación manual (HU #13299, Feature #13282 C, Épica #13202). <paramref name="ReasonCode"/> es el código
/// de la lista cerrada (<see cref="ManualRejectionReasons"/>). NO lleva tenant: el tenant sale de LA FILA.
/// </summary>
public sealed record RechazarValidacionManualCommand(Guid ValidationId, Guid ReviewedByUserId, string? ReasonCode);

/// <summary>
/// Estado resultante del rechazo. Tras rechazar la fila queda en <c>rechazado</c> (visible como «Rechazada») con
/// <c>RejectionReasonCode</c> y un enlace nuevo vigente hasta <c>LinkExpiresAt</c> para repetir la captura. Sin token: el enlace en
/// claro solo lo recibe <see cref="IManualCaptureLinkNotifier"/>. <c>EmailEnviado</c> = false avisa de que el correo no salió (el
/// Super Admin puede regenerar el enlace).
/// </summary>
public sealed record RechazarValidacionManualResult(
    Guid ValidationId,
    Guid TenantId,
    Guid? ProcedureInstanceId,
    string Status,
    string RejectionReasonCode,
    DateTimeOffset ReviewedAt,
    DateTimeOffset LinkExpiresAt,
    bool EmailEnviado);

/// <summary>
/// Rechaza una validación en <c>pendiente_revision_manual</c> con un motivo de la lista cerrada (decisión del PO: la fila QUEDA en
/// <c>rechazado</c>, no vuelve a <c>manual_activo</c>): registra <c>rejection_reason_code</c>, <c>reviewed_by</c> y
/// <c>reviewed_at</c>; en la misma operación emite un enlace nuevo de 24 h (solo su hash SHA-256 se guarda) y envía al cliente el
/// correo con el motivo en texto legible y el enlace. Con ese enlace el cliente repite la captura estando <c>rechazado</c> (la
/// sesión de captura lo acepta, HU #13299). Sin tope de intentos. El motivo y la revisión se conservan hasta que la siguiente
/// captura los limpie (<c>RegistrarCapturaManual</c>).
/// Audita <c>manual_rechazado</c> (usuario y código; sin texto libre ni PII). El fallo del correo NO revierte el rechazo: se audita
/// <c>manual_correo_fallido</c> y el resultado trae <c>EmailEnviado = false</c>.
/// <para>Errores: <c>motivo_invalido</c> (400; ausente o fuera de la lista, no se toca nada), <c>not_found</c> (404),
/// <c>estado_invalido</c> (409), <c>tramite_inactivo</c> (409; trámite anulado o revocado).</para>
/// </summary>
public sealed class RechazarValidacionManualHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationAuditLog audit,
    IManualCaptureLinkNotifier notifier,
    TimeProvider? clock = null)
{
    public const string MotivoInvalido = "motivo_invalido";
    public const string NoEncontrada = "not_found";
    public const string EstadoInvalido = "estado_invalido";
    public const string TramiteInactivo = "tramite_inactivo";

    public async Task<(RechazarValidacionManualResult? Result, string? Error)> HandleAsync(
        RechazarValidacionManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // El motivo se valida primero: un motivo ausente o ajeno a la lista no cambia nada (ni siquiera se lee la fila).
        if (!ManualRejectionReasons.IsValid(command.ReasonCode))
            return (null, MotivoInvalido);
        var reasonCode = command.ReasonCode;
        var reasonLabel = ManualRejectionReasons.LabelFor(reasonCode)!;

        var v = await repo.GetBiometricByIdAsync(command.ValidationId, ct).ConfigureAwait(false);
        if (v is null)
            return (null, NoEncontrada);

        if (!v.PuedeRevisarManual)
            return (null, EstadoInvalido);

        if (v.CongeladaPorTramite)
            return (null, TramiteInactivo);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var token = BiometricToken.Generate();
        v.RechazarRevisionManual(command.ReviewedByUserId, reasonCode, now, BiometricToken.Hash(token));
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualRechazado, IdentityValidationAuditOutcomes.Rejected,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id, PartyRole: v.PartyRole,
            Message: $"El usuario {command.ReviewedByUserId} rechazó la validación manual (motivo {reasonCode}); "
                + $"se emitió un enlace nuevo para repetir la captura, de {BiometricRules.TokenTtlHoras} h.",
            Detail: $"usuario={command.ReviewedByUserId}; motivo={reasonCode}; expira_at={v.ExpiresAt:O}"), ct).ConfigureAwait(false);

        // El token en claro sale de aquí solo hacia el puerto. Si el correo no sale, el rechazo ya confirmado NO se revierte.
        var emailEnviado = await ManualCaptureLinkDelivery
            .SendAsync(notifier, audit, v, token, ct, reasonLabel).ConfigureAwait(false);

        return (new RechazarValidacionManualResult(
            v.Id, v.TenantId, v.ProcedureInstanceId, v.Status, reasonCode!, now, v.ExpiresAt, emailEnviado), null);
    }
}
