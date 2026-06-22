namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Snapshot documental inmutable de un trámite — <c>tramites.procedure_document_snapshots</c>
/// (DDL HU #10155, RF19). Captura la matriz documental resuelta vigente al crear el trámite
/// como JSON (<see cref="Snapshot"/>, columna jsonb). Relación 1:1 con
/// <see cref="ProcedureInstance"/> (<c>procedure_instance_id</c> único). Nunca se actualiza
/// tras la creación (HU #10197).
/// </summary>
public sealed class ProcedureDocumentSnapshot
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    /// <summary>Matriz documental congelada, serializada como JSON (columna jsonb).</summary>
    public string Snapshot { get; set; } = string.Empty;

    public DateTimeOffset SnapshotAt { get; set; }

    public Guid? SnapshotBy { get; set; }
}
