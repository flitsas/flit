namespace Flit.Admin.Application.ProcedureInstances.CreateProcedureInstance;

/// <summary>
/// Cuerpo del alta de un trámite (HU #10197, AC1). <c>transitOfficeId</c> es opcional
/// (afecta la precedencia OT de la matriz); <c>createdByUserId</c> puede omitirse y
/// resolverse desde el JWT en el endpoint.
/// </summary>
public sealed record CreateProcedureInstanceRequest(
    Guid ProcedureTypeId,
    Guid TenantId,
    Guid? TransitOfficeId,
    string? ReferenceNumber,
    Guid? CreatedByUserId);
