using System.Text.Json;
using Flit.Tramites.Application.Tracking;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #10536 — marca o desmarca un trámite como prioritario para que el OT lo revise con primacía.
/// Solo cambia el flag de ordenamiento (<c>procedure_instances.prioritario</c>): NO altera el estado
/// del ciclo de vida ni los gates (RF04), por eso está disponible en cualquier estado no eliminado.
/// Idempotente: si el flag ya tiene el valor pedido, no re-persiste.
///
/// HU #14177 — cuando el flag cambia se escribe un evento <c>dato_modificado</c> en la bitácora
/// con etiqueta «Trámite prioritario», anterior «Sí»/«No», nuevo el valor contrario. Si ya tenía
/// el valor pedido no se genera fila (idempotencia cubierta por la guarda).
///
/// Código de error: <c>not_found</c> (la instancia no existe o no pertenece al tenant).
/// </summary>
public sealed class SetPriorityProcedureInstanceHandler(IProcedureInstanceRepository repo)
{
    // camelCase para el payload — mismo options que el fallback de actores (HU #14172).
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Overload compatible con las llamadas existentes que pasan <c>ct</c> en la última posición
    /// (CA1068). Delega en la sobrecarga principal con <c>actorUserId = null</c>.
    /// </summary>
    public Task<(SetPriorityResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        bool prioritario,
        CancellationToken ct = default) =>
        HandleAsync(id, tenantId, prioritario, actorUserId: null, ct);

    /// <param name="actorUserId">
    /// Usuario que realiza el cambio (claim <c>sub</c>). <c>null</c> cuando no hay sesión de
    /// usuario — el evento queda sin <c>CreatedBy</c> y se mostrará como «sistema».
    /// </param>
    public async Task<(SetPriorityResult? Result, string? Error)> HandleAsync(
        Guid id,
        Guid tenantId,
        bool prioritario,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        var instance = await repo.GetByIdAsync(id, tenantId, ct);
        if (instance is null)
            return (null, "not_found");

        if (instance.Prioritario != prioritario)
        {
            // HU #14177 — bitácora antes de actualizar para capturar el valor anterior.
            var anterior = instance.Prioritario ? "Sí" : "No";
            var nuevo    = prioritario           ? "Sí" : "No";

            instance.Prioritario = prioritario;
            instance.UpdatedAt   = DateTimeOffset.UtcNow;

            var evt = new ProcedureInstanceEvent
            {
                Id                  = Guid.NewGuid(),
                TenantId            = tenantId,
                ProcedureInstanceId = instance.Id,
                Tipo                = ProcedureFieldChangeRecorder.EventType,
                Payload             = JsonSerializer.Serialize(
                    new { etiqueta = "Trámite prioritario", campo = "prioritario", anterior, nuevo },
                    s_json),
                CreatedAt           = DateTimeOffset.UtcNow,
                CreatedBy           = actorUserId,
            };
            await repo.AddEventAsync(evt, ct);
            await repo.SaveChangesAsync(ct);
        }

        return (new SetPriorityResult(instance.Id, instance.Prioritario), null);
    }
}

/// <summary>Resultado del marcado de prioridad (contrato con el frontend: id + estado del flag).</summary>
public sealed record SetPriorityResult(Guid Id, bool Prioritario);
