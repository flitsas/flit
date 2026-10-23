using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de regeneración del enlace de captura manual (HU #13287, Feature #13280, Épica #13202). Como la activación, NO lleva
/// tenant: el Super Admin opera sobre cualquier compañía y el tenant sale de LA FILA.
/// </summary>
public sealed record RegenerarEnlaceManualCommand(Guid ValidationId, Guid RegeneratedByUserId);

/// <summary>Estado resultante. Sin token: el enlace en claro solo lo recibe <see cref="IManualCaptureLinkNotifier"/>.
/// <c>EmailEnviado</c> = false avisa de que el correo no salió (el Super Admin puede volver a regenerar).</summary>
public sealed record RegenerarEnlaceManualResult(
    Guid ValidationId,
    Guid TenantId,
    Guid? ProcedureInstanceId,
    string Provider,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ActivatedAt,
    bool EmailEnviado);

/// <summary>
/// Regenera el enlace de captura de una validación en <c>manual_activo</c>: emite un token nuevo (guardado solo como hash SHA-256,
/// vigente 24 h desde ahora) que REEMPLAZA al anterior — la búsqueda por hash ya no encuentra el token viejo, así que el público
/// recibe el mismo 404 que ante un enlace inválido: sin una columna nueva no se distingue «reemplazado» de «inválido», y el
/// front lo cubre con el mensaje «enlace inválido o reemplazado». Envía el correo (una vez) y audita sin PII ni token.
/// <para>Errores: <c>not_found</c> (404), <c>flujo_manual_no_activo</c> (409).</para>
/// </summary>
public sealed class RegenerarEnlaceManualHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationAuditLog audit,
    IManualCaptureLinkNotifier notifier,
    TimeProvider? clock = null)
{
    public const string NoEncontrada = "not_found";
    public const string FlujoManualNoActivo = "flujo_manual_no_activo";

    public async Task<(RegenerarEnlaceManualResult? Result, string? Error)> HandleAsync(
        RegenerarEnlaceManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Sin filtro de tenant a propósito (ver el comando): la fila trae el tenant dueño.
        var v = await repo.GetBiometricByIdAsync(command.ValidationId, ct).ConfigureAwait(false);
        if (v is null)
            return (null, NoEncontrada);

        if (!v.PuedeRegenerarEnlaceManual)
            return (null, FlujoManualNoActivo);

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var token = BiometricToken.Generate();
        v.RegenerarEnlaceManual(now, BiometricToken.Hash(token));
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualEnlaceRegenerado, IdentityValidationAuditOutcomes.Ok,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id, PartyRole: v.PartyRole,
            Message: $"Enlace de captura manual regenerado por el usuario {command.RegeneratedByUserId}; el anterior dejó de valer; "
                + $"vigente {BiometricRules.TokenTtlHoras} h.",
            Detail: $"usuario={command.RegeneratedByUserId}; expira_at={v.ExpiresAt:O}; reenvios_acumulados={v.ResendCount}"),
            ct).ConfigureAwait(false);

        var emailEnviado = await ManualCaptureLinkDelivery.SendAsync(notifier, audit, v, token, ct).ConfigureAwait(false);

        return (new RegenerarEnlaceManualResult(
            v.Id, v.TenantId, v.ProcedureInstanceId, v.Provider, v.Status, v.ExpiresAt, v.ManualActivatedAt, emailEnviado), null);
    }
}
