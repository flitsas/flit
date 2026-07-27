using System.Text.Json;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;

/// <summary>
/// Implementación del puerto de HISTORIAL (HU-2, RF05). Por cada transición registra EXACTAMENTE
/// una fila en <c>procedure_instance_status_history</c> (from/to, usuario, fecha, motivo) y un
/// evento <c>cambio_estado</c> en la bitácora <c>procedure_instance_events</c>. AGREGA a la unidad
/// de trabajo actual SIN SaveChanges: el commit (y la atomicidad RNF01) es responsabilidad del
/// <see cref="ITramiteLifecycleService"/> que orquesta la transición.
/// </summary>
public sealed class TramiteTransitionRecorder(IProcedureInstanceRepository repo) : ITramiteTransitionRecorder
{
    /// <summary>Tipo del evento de bitácora que genera cada transición de estado.</summary>
    public const string EventoTipo = "cambio_estado";

    public async Task RecordAsync(TramiteTransitionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // HU #10431 — guarda FK de changed_by contra identity.users: si el sujeto no existe
        // (proceso automático o claim sub inválido) se registra null sin romper la inserción.
        var changedBy = record.ChangedByUserId is { } cb && cb != Guid.Empty
            && await repo.UserExistsAsync(cb, ct).ConfigureAwait(false)
            ? record.ChangedByUserId
            : null;

        repo.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = record.TenantId,
            ProcedureInstanceId = record.ProcedureInstanceId,
            FromStatus = record.FromStatus,
            ToStatus = record.ToStatus,
            ChangedAt = record.ChangedAt,
            ChangedBy = changedBy,
            Reason = record.Reason,
            // HU #10871 — checklist HÍBRIDO de subsanación (u otro metadata futuro) construido por el
            // caller (ver TramiteTransitionCommand.Metadata). Sin metadata, se conserva '{}' (default
            // de la columna, comportamiento previo a la HU).
            Metadata = record.Metadata ?? "{}",
        });

        await repo.AddEventAsync(new ProcedureInstanceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = record.TenantId,
            ProcedureInstanceId = record.ProcedureInstanceId,
            Tipo = EventoTipo,
            Payload = JsonSerializer.Serialize(new
            {
                from_status = record.FromStatus,
                to_status = record.ToStatus,
                reason = record.Reason,
            }),
            CreatedAt = record.ChangedAt,
            CreatedBy = changedBy,
        }, ct).ConfigureAwait(false);
    }
}
