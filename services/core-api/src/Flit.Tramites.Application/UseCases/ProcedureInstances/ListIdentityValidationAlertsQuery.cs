using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Categoría de alerta ACCIONABLE de una validación de identidad (HU #10873, AC1). Se deriva SIEMPRE de
/// datos ya persistidos — nunca de un estado nuevo: <see cref="Rechazada"/> (<c>Status = rechazado</c>),
/// <see cref="Expirada"/> (token/captura vencidos sin resolver, o una identidad APROBADA cuya vigencia ya
/// se agotó — ver nota de <see cref="ListIdentityValidationAlertsHandler.Classify"/>), <see cref="PorVencer"/>
/// (aprobada vigente a ≤ <see cref="BiometricRules.VigenciaPorVencerDias"/> días de vencer, MISMO umbral que
/// <c>BiometricRules</c>, no uno nuevo) y <see cref="Atascada"/> (evento dead-letter, mismo criterio que
/// <c>GET /identity-validation/stuck</c>). <c>null</c> = sin alerta.
/// </summary>
public static class IdentityValidationAlertKinds
{
    public const string Rechazada = "rechazada";
    public const string Expirada = "expirada";
    public const string PorVencer = "por_vencer";
    public const string Atascada = "atascada";
}

/// <summary>
/// Fila de la vista de alertas/recordatorios de validación de identidad (HU #10873): una validación con su
/// clasificación accionable (AC1) y si amerita recordatorio de reenvío (AC2). <c>RecipientUserId</c> es el
/// OPERADOR (<c>ProcedureInstance.CreatedByUserId</c>) — el destinatario "supervisor" del AC no tiene
/// soporte de datos hoy (ver nota de alcance del handler) y queda fuera de esta entrega.
/// </summary>
public sealed record IdentityValidationAlertDto(
    Guid Id,
    // NULLABLE desde la integración con el Feature #10864 (HU #10865): una validación puede ser una
    // PREVALIDACIÓN STANDALONE, sin trámite (ProcedureInstanceId IS NULL). La vista de tenant
    // (HandleTenantAsync) las incluye, y son igual de accionables (rechazada/expirada/por_vencer) que
    // las de un trámite. Se reporta null en vez de Guid.Empty para no fabricar un id de trámite
    // inexistente — mismo criterio que ReferenceNumber/RecipientUserId, que ya degradan a vacío.
    Guid? InstanceId,
    string ReferenceNumber,
    Guid RecipientUserId,
    string? PartyRole,
    string Name,
    string DocumentType,
    string DocumentNumber,
    string Status,
    string? AlertKind,
    bool RequiresResendReminder,
    int? DaysRemainingVigencia,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

/// <summary>Respuesta: solo las validaciones que amerita alertar y/o recordar (sin ruido de las que están al día).</summary>
public sealed record IdentityValidationAlertsResponse(
    IReadOnlyList<IdentityValidationAlertDto> Alerts,
    int Total);

/// <summary>
/// Clasifica las validaciones de identidad en los estados accionables del AC1 (<c>rechazada</c> /
/// <c>expirada</c> / <c>por_vencer</c> / <c>atascada</c>) y marca cuáles ameritan recordatorio de reenvío
/// (AC2: <c>pendiente</c> / <c>por_vencer</c>) — HU #10873 "Alertas y recordatorios de validación de
/// identidad".
///
/// <para><b>DECISIÓN DE ALCANCE (cerrada con producto/usuario):</b> NO existe infraestructura de
/// notificaciones in-app (la campana de <c>Shell.tsx</c> del frontend es decorativa, badge hardcodeado) NI
/// un destinatario "supervisor" en el trámite (solo existe <c>ProcedureInstance.CreatedByUserId</c> =
/// operador). Por eso esta HU se entrega POR PULL: este query/endpoint de solo lectura clasifica y el
/// frontend (HU #10875) lo consume para pintar la vista consolidada del operador. El "supervisor" del AC
/// queda FUERA DE ALCANCE por falta de infraestructura — NO se construye tabla de notificaciones, outbox de
/// push ni websockets.</para>
///
/// <para>Reutiliza EXACTAMENTE la vigencia de <see cref="BiometricRules"/> (<c>VigenciaDias</c> /
/// <c>VigenciaPorVencerDias</c>, vía <see cref="BiometricRules.EsAprobadaVigente"/> /
/// <see cref="BiometricRules.DiasRestantesVigencia"/>) y el detector de atascadas de
/// <see cref="IIdentityValidationOutboxRepository"/> (mismo criterio que <c>GET /identity-validation/stuck</c>):
/// no duplica constantes ni umbrales nuevos.</para>
/// </summary>
public sealed class ListIdentityValidationAlertsHandler(
    IProcedureInstanceRepository repo,
    IIdentityValidationOutboxRepository outboxRepo)
{
    /// <summary>Cap de validaciones escaneadas para la vista de TENANT (monitoreo operativo, no histórico
    /// completo) — mismo criterio de acotación que <see cref="ListTenantBiometricValidationsHandler"/>.</summary>
    public const int MaxRows = 500;

    /// <summary>
    /// Vista consolidada del TENANT (todas las instancias, todas las partes) — la que consumirá el
    /// submódulo de Validaciones de Identidad (HU #10875). Solo lectura, acotada a <see cref="MaxRows"/>.
    /// </summary>
    public async Task<IdentityValidationAlertsResponse> HandleTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await repo.ListBiometricValidationsByTenantAsync(tenantId, 0, MaxRows, filter: null, now, ct);
        var stuckIds = await StuckIdsAsync(tenantId, ct);
        return Build(rows, stuckIds, now);
    }

    /// <summary>Vista de UN trámite puntual (p.ej. detalle del trámite): alertas de sus propias partes.</summary>
    public async Task<(IdentityValidationAlertsResponse? Result, string? Error)> HandleInstanceAsync(
        Guid instanceId, Guid tenantId, CancellationToken ct = default)
    {
        var instance = await repo.GetByIdWithBiometricsAndActorsAsync(instanceId, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        var now = DateTimeOffset.UtcNow;
        var stuckIds = await StuckIdsAsync(tenantId, ct);
        return (Build(instance.BiometricValidations, stuckIds, now), null);
    }

    /// <summary>ValidationIds atascados (dead-letter) del tenant, MISMA fuente que <c>/identity-validation/stuck</c>.</summary>
    private async Task<IReadOnlySet<Guid>> StuckIdsAsync(Guid tenantId, CancellationToken ct)
    {
        var stuck = await outboxRepo.ListStuckAsync(tenantId, ListStuckIdentityValidationsHandler.MaxRows, ct);
        return stuck.Select(s => s.ValidationId).ToHashSet();
    }

    private static IdentityValidationAlertsResponse Build(
        IEnumerable<ProcedureInstanceBiometricValidation> rows,
        IReadOnlySet<Guid> stuckIds,
        DateTimeOffset now)
    {
        var dtos = rows
            .Select(v => ToDto(v, stuckIds, now))
            // Solo lo accionable (AC1) y/o lo que amerita recordatorio (AC2) — sin ruido de lo que está al día.
            .Where(d => d.AlertKind is not null || d.RequiresResendReminder)
            .OrderBy(d => d.CreatedAt)
            .ToList();
        return new IdentityValidationAlertsResponse(dtos, dtos.Count);
    }

    internal static IdentityValidationAlertDto ToDto(
        ProcedureInstanceBiometricValidation v, IReadOnlySet<Guid> stuckIds, DateTimeOffset now)
    {
        var (alertKind, reminder) = Classify(v, stuckIds, now);
        return new IdentityValidationAlertDto(
            v.Id,
            v.ProcedureInstanceId,
            v.ProcedureInstance?.ReferenceNumber ?? string.Empty,
            v.ProcedureInstance?.CreatedByUserId ?? Guid.Empty,
            v.PartyRole,
            v.Name,
            v.DocumentType,
            v.DocumentNumber,
            v.Status,
            alertKind,
            reminder,
            BiometricRules.DiasRestantesVigencia(v, now),
            v.ExpiresAt,
            v.CreatedAt);
    }

    /// <summary>
    /// Clasificación AC1 (alerta) + AC2 (recordatorio de reenvío). Prioridad: atascada &gt; rechazada &gt;
    /// expirada (token o vigencia agotada) &gt; por_vencer &gt; pendiente (sin alerta, solo recordatorio).
    /// </summary>
    private static (string? AlertKind, bool RequiresResendReminder) Classify(
        ProcedureInstanceBiometricValidation v, IReadOnlySet<Guid> stuckIds, DateTimeOffset now)
    {
        // Atascada (dead-letter): mismo criterio que IIdentityValidationOutboxRepository.ListStuckAsync
        // (cola de envío agotada o encadenamiento firma/FUR agotado). Requiere reencolar, no reenviar.
        if (stuckIds.Contains(v.Id))
            return (IdentityValidationAlertKinds.Atascada, false);

        if (v.Status == BiometricEstados.Rechazado)
            return (IdentityValidationAlertKinds.Rechazada, false);

        if (v.Status == BiometricEstados.Aprobado)
        {
            if (BiometricRules.EsAprobadaVigente(v, now))
            {
                var dias = BiometricRules.DiasRestantesVigencia(v, now);
                return dias is not null && dias <= BiometricRules.VigenciaPorVencerDias
                    ? (IdentityValidationAlertKinds.PorVencer, true)
                    : (null, false);
            }

            // Identidad APROBADA cuya vigencia ya se agotó: se rotula "expirada" (AC1) — no hay "reenvío"
            // posible (hay que revalidar desde cero), por eso NO dispara recordatorio de reenvío (AC2).
            return (IdentityValidationAlertKinds.Expirada, false);
        }

        // Token/captura vencidos sin resolver: Status=expirado, o (aún no terminal) ya pasó ExpiresAt —
        // MISMO criterio que el flag `Expired` de ToDto en ListTenantBiometricValidationsQuery.
        if (v.Status == BiometricEstados.Expirado || now > v.ExpiresAt)
            return (IdentityValidationAlertKinds.Expirada, false);

        // "Pendiente" (AC2): aún abierta, esperando que la persona complete la captura o que el envío al
        // proveedor prospere (pendiente_envio | enviado | en_proceso | error_envio sin dead-letter todavía).
        // Reenviar el enlace/recordatorio SÍ tiene sentido aquí. Sin alerta (AC1 no la lista), solo recordatorio.
        return (null, true);
    }
}
