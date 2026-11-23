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

    /// <summary>
    /// Pausa/reanuda un solo trámite. Devuelve la observación efectiva tras el cambio
    /// (HU #14668: keep / clear / set, también al reanudar).
    /// </summary>
    public async Task<(bool Ok, string? Error, string? PausedObservation)> HandleAsync(
        Guid id, Guid tenantId, bool paused, string? observation, Guid? changedBy, CancellationToken ct = default)
    {
        var error = await ApplyAsync(id, tenantId, paused, observation, changedBy, ct);
        if (error is not null)
            return (false, error, null);

        await repo.SaveChangesAsync(ct);
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        return (true, null, instance?.PausedObservation);
    }

    /// <summary>
    /// Pausa/reanuda en lote (paridad v1 <c>pause-unpause-massive</c>). Cada id se evalúa por separado;
    /// devuelve el detalle por trámite y persiste UNA sola vez si al menos uno fue aplicado.
    /// Soporta tres modos de observación: <c>keep</c> (no tocar paused_observation),
    /// <c>single</c> (misma observación para todos) y <c>perItem</c> (observación por id en Items).
    /// Backward-compat: sin ObservationMode se infiere single si Observation no es null, keep si es null.
    /// </summary>
    public async Task<IReadOnlyList<BulkPauseResult>> HandleBulkAsync(
        PauseProcedureInstancesBulkCommand command, Guid tenantId, Guid? changedBy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Construir lookup por id para perItem (evita O(n²) en colecciones grandes)
        var perItemLookup = command.Items is { Count: > 0 }
            ? command.Items.ToDictionary(i => i.Id, i => i.Observation)
            : null;

        // Resolver el modo efectivo (backward-compat: sin mode → single si hay observation, keep si no).
        // Comparar en minúsculas: ToLowerInvariant("perItem") == "peritem", no "perItem".
        var effectiveMode = command.ObservationMode?.ToLowerInvariant() switch
        {
            "keep" => BulkObservationMode.Keep,
            "peritem" => BulkObservationMode.PerItem,
            "single" => BulkObservationMode.SameForAll,
            _ => command.Observation is not null ? BulkObservationMode.SameForAll : BulkObservationMode.Keep,
        };

        var results = new List<BulkPauseResult>(command.Ids.Count);
        var anyApplied = false;
        foreach (var id in command.Ids)
        {
            // null = keep (no tocar PausedObservation); non-null = actualizar
            var obs = effectiveMode switch
            {
                BulkObservationMode.PerItem => perItemLookup is not null && perItemLookup.TryGetValue(id, out var v) ? v : null,
                BulkObservationMode.SameForAll => command.Observation,
                _ => null, // keep
            };

            var error = await ApplyAsync(id, tenantId, command.Paused, obs, changedBy, ct);
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

        // Semántica HU #14668 §2:
        //   - observation no-null (incluso string vacío): actualizar PausedObservation → Truncate (vacío → null).
        //   - observation null (omitida): keep — no modificar PausedObservation existente.
        // Esto aplica tanto al pausar como al reanudar para permitir cambiar la nota en ambas acciones.
        if (observation is not null)
        {
            instance.PausedObservation = Truncate(observation); // string vacío → null
        }

        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureInstanceId = id,
            Tipo = paused ? "tramite_pausado" : "tramite_reanudado",
            CreatedAt = DateTimeOffset.UtcNow,
            // El evento siempre registra el valor efectivo resultante de PausedObservation.
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

/// <summary>Modos de asignación de observación en pausa/reanudación masiva (HU #14668).</summary>
public static class BulkObservationMode
{
    /// <summary>No modificar <c>paused_observation</c> existente; solo togglear <c>is_paused</c>.</summary>
    public const string Keep = "keep";
    /// <summary>
    /// Usar <see cref="PauseProcedureInstancesBulkCommand.Observation"/> para todos los ids (<c>"single"</c>).
    /// El identificador se llama <c>SameForAll</c> para evitar CA1720 (<c>Single</c> es nombre de tipo CLR).
    /// </summary>
    public const string SameForAll = "single";
    /// <summary>Observación individual por id desde <see cref="PauseProcedureInstancesBulkCommand.Items"/>.</summary>
    public const string PerItem = "perItem";
}

/// <summary>Observación por trámite en modo <see cref="BulkObservationMode.PerItem"/> (HU #14668).</summary>
public sealed record BulkPauseItemCommand(Guid Id, string? Observation);

/// <summary>
/// Comando para pausa/reanudación masiva con soporte de observación por ítem (HU #14668).
/// Backward-compat: sin <see cref="ObservationMode"/>, se infiere el modo a partir de <see cref="Observation"/>.
/// </summary>
public sealed record PauseProcedureInstancesBulkCommand(
    IReadOnlyList<Guid> Ids,
    bool Paused,
    string? ObservationMode = null,
    string? Observation = null,
    IReadOnlyList<BulkPauseItemCommand>? Items = null);
