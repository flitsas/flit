using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Orden de aprobar una validación manual (HU #13298, Feature #13282 C, Épica #13202). Como la activación, NO lleva tenant: el Super
/// Admin opera sobre cualquier compañía y el tenant sale de LA FILA.
/// </summary>
public sealed record AprobarValidacionManualCommand(Guid ValidationId, Guid ReviewedByUserId);

/// <summary>Estado resultante de la aprobación manual (vigencia siempre de <see cref="BiometricRules.VigenciaDias"/> días).</summary>
public sealed record AprobarValidacionManualResult(
    Guid ValidationId,
    Guid TenantId,
    Guid? ProcedureInstanceId,
    string Status,
    string ApprovalOrigin,
    DateTimeOffset ValidatedAt,
    DateTimeOffset ValidUntil,
    DateTimeOffset ReviewedAt);

/// <summary>
/// Aprueba una validación en <c>pendiente_revision_manual</c>. La aprobación pasa por el MISMO punto único que la de Kyverum
/// (<see cref="IdentityValidationResultApplier"/>): llama a <see cref="ProcedureInstanceBiometricValidation.Approve"/> (ValidatedAt =
/// ahora, ValidUntil = +30 días: la fecha NO se duplica aquí) y encola el evento <c>IdentityValidationCompleted</c> en la outbox,
/// que el worker entrega a <see cref="IdentityValidationCompletedConsumer"/> (firma/FUR/sello de los trámites pendientes de esa
/// persona). El gate de radicación y el reuso por tenant+documento leen la validación aprobada y vigente tal cual, sin cambios.
/// Después sella el origen <c>manual</c> y quién/cuándo revisó, y audita <c>manual_aprobado</c> sin PII ni rutas.
/// <para>Errores: <c>not_found</c> (404), <c>estado_invalido</c> (409; no está pendiente de revisión), <c>tramite_inactivo</c> (409;
/// trámite anulado o revocado: la validación se conserva, Bug #13055).</para>
/// </summary>
public sealed class AprobarValidacionManualHandler(
    IProcedureInstanceRepository repo,
    IdentityValidationResultApplier applier,
    IIdentityValidationAuditLog audit,
    TimeProvider? clock = null)
{
    public const string NoEncontrada = "not_found";
    public const string EstadoInvalido = "estado_invalido";
    public const string TramiteInactivo = "tramite_inactivo";

    /// <summary>Estado crudo que se deja como trazabilidad en la fila (no hay proveedor externo).</summary>
    public const string ProviderStatusAprobadoManual = "manual_approved";

    public async Task<(AprobarValidacionManualResult? Result, string? Error)> HandleAsync(
        AprobarValidacionManualCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Sin filtro de tenant a propósito (ver el comando): la fila trae el tenant dueño y el trámite (si hay).
        var v = await repo.GetBiometricByIdAsync(command.ValidationId, ct).ConfigureAwait(false);
        if (v is null)
            return (null, NoEncontrada);

        if (!v.PuedeRevisarManual)
            return (null, EstadoInvalido);

        if (v.CongeladaPorTramite)
            return (null, TramiteInactivo);

        var now = (clock ?? TimeProvider.System).GetUtcNow();

        // Mismo camino que Kyverum: Approve + evento de completado (outbox, se confirma en el SaveChanges de abajo).
        var aplicado = await applier.ApplyAsync(
            v,
            new IdentityValidationTerminalResult(true, ProviderStatusAprobadoManual, "{\"origen\":\"manual\"}", null),
            now,
            ct).ConfigureAwait(false);
        if (!aplicado)
            return (null, EstadoInvalido);

        v.SellarAprobacionManual(command.ReviewedByUserId, now);
        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.ManualAprobado, IdentityValidationAuditOutcomes.Approved,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id, PartyRole: v.PartyRole,
            Message: $"El usuario {command.ReviewedByUserId} aprobó la validación manual; vigencia {BiometricRules.VigenciaDias} días.",
            Detail: $"usuario={command.ReviewedByUserId}; valid_until={v.ValidUntil:O}"), ct).ConfigureAwait(false);

        return (new AprobarValidacionManualResult(
            v.Id, v.TenantId, v.ProcedureInstanceId, v.Status, v.ApprovalOrigin!,
            v.ValidatedAt!.Value, v.ValidUntil!.Value, v.ReviewedAt!.Value), null);
    }
}
