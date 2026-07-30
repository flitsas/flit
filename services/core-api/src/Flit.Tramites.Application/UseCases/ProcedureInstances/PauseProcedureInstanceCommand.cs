using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Pausar / reanudar un trámite ICT desde la UI de FLIT (paridad v1: handleChangePausedState del detalle
/// + pause-unpause-massive de la lista). Solo aplica a BORRADORES originados por ICT (<c>origin='ict'</c>):
/// setea <c>is_paused</c> + <c>paused_observation</c> (informativa) y registra un evento en la bitácora.
/// La radicación ya se bloquea aparte en <see cref="SubmitProcedureInstanceHandler"/> cuando
/// <c>is_paused=true</c>. NO notifica al gestor: la observación de pausa es informativa (sin webhook),
/// igual que en v1. Errores: <c>not_found</c> | <c>not_ict</c> | <c>not_borrador</c>.
/// </summary>
public sealed class PauseProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    private const string IctOrigin = "ict";
    private const int MaxObservationLength = 250;

    /// <summary>Pausa/reanuda un solo trámite.</summary>
    public async Task<(bool Ok, string? Error)> HandleAsync(
        Guid id, Guid tenantId, bool paused, string? observation, Guid? changedBy, CancellationToken ct = default)
    {
        var error = await ApplyAsync(id, tenantId, paused, observation, changedBy, ct);
        if (error is not null)
            return (false, error);

        await repo.SaveChangesAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Pausa/reanuda en lote (paridad v1 <c>pause-unpause-massive</c>). Cada id se evalúa por separado;
    /// devuelve el detalle por trámite y persiste UNA sola vez si al menos uno fue aplicado.
    /// </summary>
    public async Task<IReadOnlyList<BulkPauseResult>> HandleBulkAsync(
        IReadOnlyList<Guid> ids, Guid tenantId, bool paused, string? observation, Guid? changedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        var results = new List<BulkPauseResult>(ids.Count);
        var anyApplied = false;
        foreach (var id in ids)
        {
            var error = await ApplyAsync(id, tenantId, paused, observation, changedBy, ct);
            results.Add(new BulkPauseResult(id, error is null, error));
            anyApplied |= error is null;
        }

        if (anyApplied)
            await repo.SaveChangesAsync(ct);
        return results;
    }

    /// <summary>Aplica el cambio a la instancia rastreada SIN persistir (el caller hace un único SaveChanges).</summary>
    private async Task<string?> ApplyAsync(
        Guid id, Guid tenantId, bool paused, string? observation, Guid? changedBy, CancellationToken ct)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        if (instance is null)
            return "not_found";
        if (!string.Equals(instance.Origin, IctOrigin, StringComparison.OrdinalIgnoreCase))
            return "not_ict";
        if (instance.Status != TramiteEstado.Borrador)
            return "not_borrador";

        instance.IsPaused = paused;
        instance.PausedObservation = paused ? Truncate(observation) : null;

        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = paused ? "tramite_pausado" : "tramite_reanudado",
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = JsonSerializer.Serialize(new { observation = instance.PausedObservation, changedBy }),
        }, ct);

        return null;
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= MaxObservationLength ? value : value[..MaxObservationLength];
}

/// <summary>Resultado por trámite de una operación masiva de pausa/reanudación.</summary>
public sealed record BulkPauseResult(Guid Id, bool Ok, string? Error);
