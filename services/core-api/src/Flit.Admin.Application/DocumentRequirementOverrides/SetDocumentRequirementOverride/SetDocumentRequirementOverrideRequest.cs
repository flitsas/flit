namespace Flit.Admin.Application.DocumentRequirementOverrides.SetDocumentRequirementOverride;

/// <summary>
/// Payload del upsert de obligatoriedad por OT (HU #10198). Se identifica por la tupla
/// natural (trámite, documento, OT); no hay id de cliente porque el override no se conoce
/// por id desde la UI. <c>estado</c> = REQUIRED | OPTIONAL | NOT_APPLICABLE | DEFAULT, donde
/// DEFAULT limpia el override (el documento vuelve a heredar el default del trámite).
/// </summary>
/// <param name="ProcedureTypeId">Tipo de trámite (debe existir).</param>
/// <param name="DocumentTypeId">Tipo de documento (debe existir y estar asociado al trámite).</param>
/// <param name="TransitOfficeId">Organismo de tránsito (debe existir).</param>
/// <param name="Estado">REQUIRED | OPTIONAL | NOT_APPLICABLE | DEFAULT.</param>
public sealed record SetDocumentRequirementOverrideRequest(
    Guid ProcedureTypeId,
    Guid DocumentTypeId,
    Guid TransitOfficeId,
    string? Estado);
