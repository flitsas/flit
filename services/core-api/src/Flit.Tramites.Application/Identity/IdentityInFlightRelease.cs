using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Bug #13885 — libera el documento ANTES de crear una validación nueva. El índice único
/// <c>uq_biometric_validations_inflight_doc_norm</c> admite una sola validación en vuelo por (tenant, documento), y la
/// precedencia de envío solo mira la fila más reciente de la persona: una fila en vuelo de un trámite anulado, revocado o
/// borrado (congelada por Bug #13055, que nunca se cerraba) dejaba pasar la decisión pero hacía fallar el guardado, y como
/// el proveedor ya se había llamado quedaba una verificación huérfana en Kyverum por cada intento.
/// <para>
/// Aquí se consultan TODAS las filas que bloquearían el guardado. Las de un trámite inactivo se cierran como
/// <c>expirado</c> (sin aplicar veredicto del proveedor) y quedan en la bitácora; la primera fila viva que quede se devuelve
/// para que el caso de uso responda 409 SIN llamar al proveedor.
/// </para>
/// </summary>
public static class IdentityInFlightRelease
{
    /// <summary>
    /// Cierra las filas en vuelo de trámites inactivos del documento y devuelve la primera fila en vuelo VIVA que siga
    /// bloqueando, o <c>null</c> si el documento quedó libre. Persiste el cierre (<c>SaveChanges</c>) solo si cerró alguna.
    /// </summary>
    public static async Task<ProcedureInstanceBiometricValidation?> LiberarAsync(
        IProcedureInstanceRepository repo,
        IIdentityValidationAuditLog? audit,
        Guid tenantId,
        string tipoDoc,
        string documento,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(repo);

        var bloqueos = await repo.ListInFlightBlockersByDocumentAsync(tenantId, tipoDoc, documento, ct).ConfigureAwait(false);
        var cerradas = new List<(ProcedureInstanceBiometricValidation Fila, string EstadoPrevio)>();
        ProcedureInstanceBiometricValidation? viva = null;
        foreach (var v in bloqueos)
        {
            if (BiometricRules.EsEnVueloDeTramiteInactivo(v))
            {
                cerradas.Add((v, v.Status));
                v.Status = BiometricEstados.Expirado;
                v.UpdatedAt = now;
            }
            else
            {
                viva ??= v;
            }
        }

        if (cerradas.Count == 0)
            return viva;

        await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        if (audit is not null)
        {
            foreach (var (fila, estadoPrevio) in cerradas)
                await audit.LogAsync(CierreAuditado(fila, estadoPrevio, "Se cierra al iniciar una validación nueva del mismo documento"), ct)
                    .ConfigureAwait(false);
        }

        return viva;
    }

    /// <summary>
    /// Conflicto informativo cuando una fila VIVA bloquea: a diferencia de
    /// <see cref="IdentitySendDecisionForTramite.InFlightRaceConflict"/>, lleva el estado y el origen reales (el frontend
    /// mostraba «Estado: disponible» porque el conflicto llegaba sin estado).
    /// </summary>
    public static IdentitySendDecision Conflicto(ProcedureInstanceBiometricValidation viva)
    {
        ArgumentNullException.ThrowIfNull(viva);
        return new IdentitySendDecision(
            IdentitySendDecisionKind.NoEnviar,
            IdentitySendMotivo.ValidacionEnVuelo,
            Status: viva.Status,
            ValidationId: viva.Id,
            Origen: viva.ProcedureInstanceId is null ? IdentitySendOrigen.Standalone : IdentitySendOrigen.Tramite);
    }

    /// <summary>Entrada de bitácora del cierre de una fila en vuelo de un trámite inactivo (sin PII).</summary>
    public static IdentityValidationAuditEntry CierreAuditado(
        ProcedureInstanceBiometricValidation fila, string estadoPrevio, string motivo) =>
        new(
            IdentityValidationAuditStages.Expired, IdentityValidationAuditOutcomes.Expired,
            TenantId: fila.TenantId, ProcedureInstanceId: fila.ProcedureInstanceId, ValidationId: fila.Id,
            KyverumVerificationId: fila.KyverumVerificationId, PartyRole: fila.PartyRole,
            Message: $"{motivo}: el trámite está {EstadoTramite(fila)} y la validación en vuelo ya no puede usarse.",
            Detail: $"estado_previo={estadoPrevio}; expires_at={fila.ExpiresAt:O}");

    private static string EstadoTramite(ProcedureInstanceBiometricValidation fila) =>
        fila.ProcedureInstance?.DeletedAt is not null ? "borrado" : fila.ProcedureInstance?.Status ?? "inactivo";
}
