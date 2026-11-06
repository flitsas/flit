using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de copiar el enlace de captura manual vigente (HU #14305, Feature #14304, Épica #14271). Como la activación, NO lleva
/// tenant: el Super Admin opera sobre cualquier compañía y el tenant sale de LA FILA.
/// </summary>
public sealed record CopiarEnlaceManualCommand(Guid ValidationId, Guid CopiedByUserId);

/// <summary>Enlace completo y vencimiento. La URL contiene el token: no se registra en logs ni en auditoría.</summary>
public sealed record CopiarEnlaceManualResult(string Url, DateTimeOffset ExpiresAt);

/// <summary>
/// Entrega al Super Admin el enlace de captura de una validación que espera la captura del cliente y cuyo enlace sigue vigente,
/// descifrando el token guardado al emitirlo. NO regenera: el token y el hash no cambian, no se envía correo. Audita quién y
/// cuándo (<c>manual_enlace_copiado</c>), sin token ni URL.
/// <para>Errores: <c>not_found</c> (404), <c>flujo_manual_no_activo</c> (409), <c>enlace_vencido</c> (409),
/// <c>enlace_no_disponible</c> (409; captura ya enviada, enlace emitido antes del cifrado, o token que no se puede descifrar).
/// En los tres 409 el Super Admin debe regenerar o esperar a la revisión.</para>
/// </summary>
public sealed class CopiarEnlaceManualHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationAuditLog audit,
    IManualCaptureTokenProtector protector,
    IManualCaptureLinkUrlBuilder urlBuilder,
    TimeProvider? clock = null)
{
    public const string NoEncontrada = "not_found";
    public const string FlujoManualNoActivo = "flujo_manual_no_activo";
    public const string EnlaceVencido = "enlace_vencido";
    public const string EnlaceNoDisponible = "enlace_no_disponible";

    public async Task<(CopiarEnlaceManualResult? Result, string? Error)> HandleAsync(
        CopiarEnlaceManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Sin filtro de tenant a propósito (ver el comando): la fila trae el tenant dueño.
        var v = await repo.GetBiometricByIdAsync(command.ValidationId, ct).ConfigureAwait(false);
        if (v is null)
            return (null, NoEncontrada);

        if (!v.EsperaCapturaManual)
        {
            // Captura ya enviada: el enlace se consumió; no hay nada que copiar.
            return (null, v.PuedeRevisarManual ? EnlaceNoDisponible : FlujoManualNoActivo);
        }

        var now = (clock ?? TimeProvider.System).GetUtcNow();
        if (now > v.ExpiresAt)
            return (null, EnlaceVencido);

        if (string.IsNullOrEmpty(v.ManualCaptureTokenEncrypted))
            return (null, EnlaceNoDisponible);

        var token = protector.TryUnprotect(v.ManualCaptureTokenEncrypted);
        if (string.IsNullOrEmpty(token))
            return (null, EnlaceNoDisponible);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualEnlaceCopiado, IdentityValidationAuditOutcomes.Ok,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id, PartyRole: v.PartyRole,
            Message: $"El usuario {command.CopiedByUserId} copió el enlace de captura manual vigente.",
            Detail: $"usuario={command.CopiedByUserId}; expira_at={v.ExpiresAt:O}"), ct).ConfigureAwait(false);

        return (new CopiarEnlaceManualResult(urlBuilder.Build(token), v.ExpiresAt), null);
    }
}
