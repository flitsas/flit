using System.Text.Json;
using Flit.Ict.Domain.Abstractions;

namespace Flit.Ict.Application.Status;

/// <summary>
/// Pausa o reanuda un trámite ya materializado (servicio v1 <c>pauseDraftProcess</c>). Resuelve el
/// pre-trámite por su manager_id_transaction y delega en core-api (gRPC <c>PauseDraft</c>), que persiste
/// la pausa en el borrador. Solo aplica si el pre-trámite ya tiene borrador (procedure_instance).
/// HU #14668 §4: tras un PauseDraft exitoso actualiza <c>master.ObservationWhenPaused</c> y registra
/// un evento de timeline (<c>pausado</c>/<c>reanudado</c>) en <c>ict.pretramite_events</c>.
/// </summary>
public sealed class PauseDraftHandler(
    IPreTramiteRepository repository,
    IProcedureDraftClient draftClient,
    ICurrentTenant currentTenant)
{
    public async Task<(bool Ok, string? Error)> HandleAsync(
        string managerIdTransaction,
        bool paused,
        string? observation,
        CancellationToken ct = default)
    {
        var tenantId = currentTenant.TenantId;
        if (tenantId is null)
        {
            return (false, "unauthenticated");
        }

        var master = await repository.FindByManagerIdTransactionAsync(managerIdTransaction, tenantId.Value, ct);
        if (master is null)
        {
            return (false, "not_found");
        }

        if (master.ProcedureInstanceId is null)
        {
            return (false, "not_materialized");
        }

        var result = await draftClient.PauseDraftAsync(
            tenantId.Value,
            master.ProcedureInstanceId.Value,
            paused,
            observation ?? string.Empty,
            master.ManagerUser,
            master.ManagerMail,
            master.CompanyManagerDocument,
            ct);

        if (result.ErrorCode is not null)
        {
            return (false, result.ErrorCode);
        }

        // HU #14668 §4 — actualizar la observación en el master ICT para que el endpoint de estado v1
        // pueda reflejar la última observación de pausa. Semántica idéntica a core-api:
        // observación vacía → null; null no-enviada → mantener valor anterior (keep).
        var effectiveObs = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
        if (effectiveObs is not null || paused)
        {
            // Al pausar siempre actualizamos (incluso si effectiveObs es null, se borra).
            // Al reanudar con observation → actualizamos. Al reanudar sin observation → keep (no SaveAsync).
            master.ObservationWhenPaused = effectiveObs;
            await repository.SaveAsync(tenantId.Value, ct);
        }

        // HU #14668 §4 — timeline ICT: pausado/reanudado con observación (texto libre, truncado a 250 chars
        // en core-api). La observation es del cliente; se incluye en el detalle según el requerimiento.
        var stage = paused ? "pausado" : "reanudado";
        var detailJson = effectiveObs is not null
            ? JsonSerializer.Serialize(new { observation = effectiveObs })
            : null;
        await repository.RecordTimelineEventAsync(master.Id, tenantId.Value, stage, "ok", detailJson, ct);

        return (true, null);
    }
}
