namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Documento individual congelado en el snapshot documental de un trámite (HU #10197,
/// RF19). Reproduce los campos de la matriz resuelta (HU #10196) al instante de creación
/// del trámite; una vez serializado en <c>tramites.procedure_document_snapshots.snapshot</c>
/// es <b>inmutable</b>: los cambios posteriores en catálogo u overrides no lo alteran.
/// </summary>
public sealed record ProcedureDocumentSnapshotItem(
    Guid DocumentTypeId,
    string Codigo,
    string Nombre,
    bool Obligatorio,
    short OrdenResuelto,
    string NivelAplicado);
