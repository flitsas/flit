namespace Flit.Admin.Application.DocumentOrderOverrides.CreateDocumentOrderOverride;

/// <summary>
/// Payload de alta de un override de orden documental (HU #10196, AC1/AC2; RF22). El
/// <c>scope</c> viaja como query (no en el body) y solo admite <c>OT</c>; la referencia es
/// el <c>transitOfficeId</c>.
/// </summary>
/// <param name="ProcedureTypeId">Tipo de trámite (debe existir).</param>
/// <param name="DocumentTypeId">Tipo de documento (debe existir y estar asociado al trámite).</param>
/// <param name="TransitOfficeId">Organismo de tránsito (obligatorio; único ámbito tras RF22).</param>
/// <param name="Orden">Orden personalizado (smallint ≥ 0).</param>
public sealed record CreateDocumentOrderOverrideRequest(
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    Guid? TransitOfficeId,
    int? Orden);
