using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de activación del flujo manual de identidad (HU #13284, Feature #13280, Épica #13202). Común a validaciones de
/// trámite y de prevalidación standalone (<c>ProcedureInstanceId</c> nulo, anclada a su persona); HU #13285 (A3) lo aplica tal cual a
/// mandatarios (fila con <c>MandateSignerId</c>, se conserva) y representante legal: sin rutas propias (las de admin están retiradas, 410). NO lleva tenant: el Super Admin opera sobre cualquier compañía y el tenant sale de LA
/// FILA, así toda escritura y auditoría usa el tenant dueño (el endpoint no confía en <c>X-Tenant-Id</c>).
/// </summary>
public sealed record ActivarIdentidadManualCommand(Guid ValidationId, Guid ActivatedByUserId);

/// <summary>Estado resultante. Sin token: el enlace en claro solo lo recibe <see cref="IManualCaptureLinkNotifier"/>.</summary>
public sealed record ActivarIdentidadManualResult(
    Guid ValidationId,
    Guid TenantId,
    Guid? ProcedureInstanceId,
    string Provider,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ActivatedAt,
    bool KyverumCancelado,
    string Origin,
    bool EmailEnviado);

/// <summary>
/// Activa el flujo manual sobre una validación que NO esté aprobada y vigente: la MISMA fila pasa a proveedor
/// <c>manual</c> / estado <c>manual_activo</c> con un token de 24 h guardado solo como hash (mismo patrón que el magic-link de
/// la biométrica, <see cref="BiometricToken"/>), cancela la verificación Kyverum en curso y deja auditoría (una fila por paso,
/// sin PII ni secretos ni token). El token en claro se entrega UNA vez al puerto <see cref="IManualCaptureLinkNotifier"/>.
/// <para>Errores: <c>not_found</c> (404), <c>identidad_aprobada_vigente</c> (409), <c>tramite_inactivo</c> (409; trámite anulado
/// o revocado, la validación se conserva — Bug #13055).</para>
/// </summary>
public sealed class ActivarIdentidadManualHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationAuditLog audit,
    IManualCaptureLinkNotifier notifier,
    TimeProvider? clock = null)
{
    public const string NoEncontrada = "not_found";
    public const string AprobadaVigente = "identidad_aprobada_vigente";
    public const string TramiteInactivo = "tramite_inactivo";

    /// <summary>Verificación Kyverum con algo en vuelo (o encolado/fallido de envío) que esta activación cancela.</summary>
    private static bool KyverumEnVuelo(ProcedureInstanceBiometricValidation v) =>
        string.Equals(v.Provider, BiometricProviders.Kyverum, StringComparison.Ordinal)
        && v.Status is BiometricEstados.Enviado or BiometricEstados.EnProceso
            or BiometricEstados.PendienteEnvio or BiometricEstados.ErrorEnvio;

    public async Task<(ActivarIdentidadManualResult? Result, string? Error)> HandleAsync(
        ActivarIdentidadManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Sin filtro de tenant a propósito (ver el comando): la fila trae el tenant dueño y trae el trámite (si hay).
        var v = await repo.GetBiometricByIdAsync(command.ValidationId, ct).ConfigureAwait(false);
        if (v is null)
            return (null, NoEncontrada);

        if (v.CongeladaPorTramite)
            return (null, TramiteInactivo);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (!v.PuedeActivarFlujoManual(now))
            return (null, AprobadaVigente);

        // HU #13285: mandatarios (MandateSignerId) y representante legal se activan igual que el resto; el origen solo se reporta.
        var origen = ManualValidationOrigin.From(v);
        var cancelaKyverum = KyverumEnVuelo(v);
        var kyverumIdPrevio = v.KyverumVerificationId;
        var estadoPrevio = v.Status;
        var proveedorPrevio = v.Provider;

        var token = BiometricToken.Generate();
        v.ActivarFlujoManual(command.ActivatedByUserId, now, BiometricToken.Hash(token));
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        // Bitácora técnica: una fila por paso. El id de Kyverum queda SOLO aquí (trazabilidad); nunca el token.
        if (cancelaKyverum)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.KyverumCanceladoPorManual, IdentityValidationAuditOutcomes.Ok,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: kyverumIdPrevio, PartyRole: v.PartyRole,
                Message: "Activación del flujo manual: la verificación Kyverum en curso queda cancelada para FLIT; "
                    + "los webhooks posteriores no aplican.",
                Detail: $"estado_previo={estadoPrevio}"), ct).ConfigureAwait(false);
        }

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualActivado, IdentityValidationAuditOutcomes.Ok,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
            KyverumVerificationId: kyverumIdPrevio, PartyRole: v.PartyRole,
            Message: $"Flujo manual activado por el usuario {command.ActivatedByUserId}; enlace de captura vigente "
                + $"{BiometricRules.TokenTtlHoras} h.",
            Detail: $"origen={origen}; usuario={command.ActivatedByUserId}; estado_previo={estadoPrevio}; proveedor_previo={proveedorPrevio}; "
                + $"kyverum_cancelado={cancelaKyverum}; expira_at={v.ExpiresAt:O}"), ct).ConfigureAwait(false);

        // El token en claro sale de aquí solo hacia el puerto (HU #13287: correo al titular). Si el correo no sale, la activación
        // ya confirmada NO se revierte: se audita 'manual_correo_fallido' y el resultado lo informa (EmailEnviado = false).
        var emailEnviado = await ManualCaptureLinkDelivery.SendAsync(notifier, audit, v, token, ct).ConfigureAwait(false);

        return (new ActivarIdentidadManualResult(
            v.Id, v.TenantId, v.ProcedureInstanceId, v.Provider, v.Status, v.ExpiresAt, now, cancelaKyverum, origen, emailEnviado), null);
    }
}
