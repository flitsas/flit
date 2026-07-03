namespace Flit.Tramites.Domain.Tramites.Estados;

/// <summary>
/// Snapshot de una transición efectuada, compartido por el historial (RF05) y la publicación (RNF01).
/// </summary>
public sealed record TramiteTransitionRecord(
    Guid TenantId,
    Guid ProcedureInstanceId,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    Guid? ChangedByUserId,
    DateTimeOffset ChangedAt);

/// <summary>
/// Puerto de HISTORIAL (HU-2, RF05): registra la transición en
/// <c>procedure_instance_status_history</c> (from/to, usuario, fecha, motivo) y el evento de
/// timeline en <c>procedure_instance_events</c>. AGREGA a la unidad de trabajo actual SIN hacer
/// SaveChanges — el commit lo hace <see cref="ITramiteLifecycleService"/> (atomicidad RNF01).
/// La implementación aplica la guarda FK de <c>changed_by</c> contra <c>identity.users</c>
/// (mismo criterio HU #10431: si el usuario no existe, se registra null).
/// </summary>
public interface ITramiteTransitionRecorder
{
    Task RecordAsync(TramiteTransitionRecord record, CancellationToken ct = default);
}

/// <summary>No-op para compilar/testear HU-1 de forma aislada; la integración registra la real.</summary>
public sealed class NullTramiteTransitionRecorder : ITramiteTransitionRecorder
{
    public Task RecordAsync(TramiteTransitionRecord record, CancellationToken ct = default) =>
        Task.CompletedTask;
}
