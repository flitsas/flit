using Flit.Tramites.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>Resultado de reconciliar: estado (posiblemente actualizado) y si hubo cambio.</summary>
public sealed record ReconciliarIdentidadResult(string Status, bool Updated);

/// <summary>
/// Reconciliación on-demand de UNA validación de identidad: consulta su estado real en Kyverum
/// (<c>GET /v1/validations/{id}</c>) y lo aplica si ya es terminal. Fallback cuando el webhook no llegó
/// (p.ej. callback mal ruteado entre ambientes): el frontend puede pollear este endpoint en la pantalla
/// "Esperando validación" para desatascar en vivo. Idempotente: si ya está terminal, no hace nada.
/// </summary>
public sealed class ReconciliarIdentidadHandler(
    IProcedureInstanceRepository repo,
    IKyverumVerifyClient kyverum,
    IdentityValidationResultApplier applier,
    IIdentityValidationAuditLog audit)
{
    public async Task<(ReconciliarIdentidadResult? Result, string? Error)> HandleAsync(
        Guid instanceId, Guid tenantId, Guid validationId, CancellationToken ct = default)
    {
        var v = await repo.GetBiometricByIdAsync(validationId, ct);
        if (v is null || v.TenantId != tenantId || v.ProcedureInstanceId != instanceId)
            return (null, "not_found");

        if (!string.Equals(v.Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(v.KyverumVerificationId))
            return (null, "no_kyverum");

        // Solo tiene sentido consultar mientras está pendiente; terminal → nada que reconciliar.
        if (v.Status is not (BiometricEstados.EnProceso or BiometricEstados.Enviado))
            return (new ReconciliarIdentidadResult(v.Status, false), null);

        KyverumVerifyStatus? status;
        try
        {
            status = await kyverum.GetStatusAsync(v.KyverumVerificationId!, v.PartyRole, ct);
        }
        catch (KyverumVerifyException ex)
        {
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Reconcile, IdentityValidationAuditOutcomes.Error,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                ErrorType: nameof(KyverumVerifyException), HttpStatus: ex.Transient ? 503 : 502,
                Message: "Reconciliación on-demand: falló la consulta de estado."), ct);
            return (null, ex.Transient ? "proveedor_no_disponible" : "proveedor_error");
        }

        if (status is null)
            return (new ReconciliarIdentidadResult(v.Status, false), null); // Kyverum no conoce el id.

        bool updated;
        try
        {
            updated = await IdentityValidationReconciler.ApplyStatusAsync(applier, v, status, DateTimeOffset.UtcNow, ct);
            if (updated)
                await repo.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Falló al aplicar/guardar el resultado (p.ej. DbUpdateException). Antes esto propagaba un 500
            // "pelado" que NO quedaba en la bitácora; ahora el error REAL sí se registra para diagnóstico.
            await audit.LogAsync(new IdentityValidationAuditEntry(
                IdentityValidationAuditStages.Reconcile, IdentityValidationAuditOutcomes.Error,
                TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
                KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
                ErrorType: ex.GetType().Name, HttpStatus: 500,
                ProviderStatus: status.Status,
                Message: "Reconciliación on-demand: falló al guardar el resultado. " + Truncate(ex.GetBaseException().Message)), ct);
            return (null, "persist_error");
        }

        await audit.LogAsync(new IdentityValidationAuditEntry(
            IdentityValidationAuditStages.Reconcile,
            updated ? v.Status : IdentityValidationAuditOutcomes.Pending,
            TenantId: v.TenantId, ProcedureInstanceId: v.ProcedureInstanceId, ValidationId: v.Id,
            KyverumVerificationId: v.KyverumVerificationId, PartyRole: v.PartyRole,
            ProviderStatus: status.Status,
            Message: updated ? "Reconciliación on-demand: estado sincronizado." : "Reconciliación on-demand: aún pendiente."), ct);

        return (new ReconciliarIdentidadResult(v.Status, updated), null);
    }

    /// <summary>Recorta el mensaje de error para la bitácora (evita textos enormes de EF/Npgsql).</summary>
    private static string Truncate(string message) =>
        string.IsNullOrEmpty(message) || message.Length <= 300 ? message : message[..300];
}
