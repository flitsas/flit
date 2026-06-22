namespace Flit.Admin.Domain.ProcedureSnapshots;

/// <summary>Read model de una instancia de trámite — <c>tramites.procedure_instances</c>.</summary>
public sealed record ProcedureInstanceRecord(
    Guid Id,
    Guid TenantId,
    Guid ProcedureTypeId,
    string ReferenceNumber,
    string Status,
    Guid? TransitOfficeId,
    DateTimeOffset CreatedAt);
