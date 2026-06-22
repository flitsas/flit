namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>
/// Datos de entrada para crear un trámite en estado borrador (HU #10197, AC1). El
/// repositorio genera el id, el estado inicial (<c>draft</c>) y los timestamps; el snapshot
/// documental se persiste en la misma transacción.
/// </summary>
public sealed record NewProcedureInstance(
    Guid TenantId,
    Guid ProcedureTypeId,
    string ReferenceNumber,
    Guid? TransitOfficeId,
    Guid CreatedByUserId);
