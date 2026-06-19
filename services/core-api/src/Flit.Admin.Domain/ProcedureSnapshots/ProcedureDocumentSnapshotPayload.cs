namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Estructura canónica del JSON persistido en
/// <c>tramites.procedure_document_snapshots.snapshot</c> (HU #10197, RF19). Captura la
/// matriz documental resuelta vigente al crear el trámite junto con el contexto que la
/// generó (tipo de trámite, organismo de tránsito y cliente). Es el modelo de dominio
/// compartido por la serialización (Application) y el guard de uso (Infrastructure).
/// </summary>
public sealed record ProcedureDocumentSnapshotPayload(
    Guid ProcedureTypeId,
    Guid? TransitOfficeId,
    Guid? ClienteId,
    IReadOnlyList<ProcedureDocumentSnapshotItem> Documentos);
