namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Read model de un snapshot documental — <c>tramites.procedure_document_snapshots</c>.
/// <see cref="SnapshotJson"/> es el JSON crudo (jsonb) con la matriz congelada; se
/// deserializa a <see cref="ProcedureDocumentSnapshotPayload"/> para exponer los documentos.
/// </summary>
public sealed record ProcedureDocumentSnapshotRecord(
    Guid Id,
    Guid ProcedureInstanceId,
    Guid TenantId,
    string SnapshotJson,
    DateTimeOffset SnapshotAt);
