using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #14177 — registra un evento <c>dato_modificado</c> para la placa asignada o corregida
/// por el organismo de tránsito (camino OT: <c>AssignPlateAsync</c> / <c>UpdatePlateAsync</c>).
///
/// <para>No toca <c>PatchFieldValuesCommand</c> (camino del gestor): ese ya pasa por
/// <see cref="ProcedureFieldChangeRecorder"/> y crea sus propios eventos. Este handler existe
/// solo para el camino OT, que persiste la placa directamente en el repositorio de OT sin
/// pasar por el recorder del gestor.</para>
///
/// <para>Si anterior y nuevo son iguales (Ordinal tras trim), no genera evento.</para>
/// </summary>
public sealed class BitacoraPlacaOtHandler(IProcedureInstanceRepository repo)
{
    // camelCase para el payload — mismo options que el fallback de actores (HU #14172).
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    private static string Normalize(string? v) =>
        string.IsNullOrWhiteSpace(v) ? string.Empty : v.Trim();

    /// <param name="tenantId">Tenant del trámite (cliente, no OT).</param>
    /// <param name="instanceId">Id de la instancia.</param>
    /// <param name="previousPlate">
    /// Placa anterior (null / vacío = primera asignación desde <c>preasignacion</c>).
    /// </param>
    /// <param name="newPlate">Nueva placa ya normalizada (trim + upper).</param>
    /// <param name="changedBy">Usuario OT que realiza la operación; null si es sistema.</param>
    /// <param name="ct">Token de cancelación (CA1068: último parámetro).</param>
    public async Task HandleAsync(
        Guid tenantId,
        Guid instanceId,
        string? previousPlate,
        string newPlate,
        Guid? changedBy,
        CancellationToken ct)
    {
        var anterior = Normalize(previousPlate);
        var nuevo    = Normalize(newPlate);

        // El mismo valor otra vez no deja fila.
        if (string.Equals(anterior, nuevo, StringComparison.Ordinal))
            return;

        var evt = new ProcedureInstanceEvent
        {
            Id                  = Guid.NewGuid(),
            TenantId            = tenantId,
            ProcedureInstanceId = instanceId,
            Tipo                = ProcedureFieldChangeRecorder.EventType,
            Payload             = JsonSerializer.Serialize(
                new { etiqueta = "Placa", campo = "plate", anterior, nuevo },
                s_json),
            CreatedAt           = DateTimeOffset.UtcNow,
            CreatedBy           = changedBy,
        };

        await repo.AddEventAsync(evt, ct);
        await repo.SaveChangesAsync(ct);
    }
}
