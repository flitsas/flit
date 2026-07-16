namespace Flit.Admin.Application.OtClientProcedures;

public sealed class OtClientProcedureResponse
{
    public Guid Id { get; init; }

    public Guid ClientTenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    public string ReferenceNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>Feature #10587 / HU #10785 — sub-estado interno de placa (null | preasignado | asignado).</summary>
    public string? PlateFlowStatus { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>HU #10536 — trámite prioritario: se ordena con primacía en la bandeja del OT.</summary>
    public bool Prioritario { get; init; }
}

public sealed class RejectOtClientProcedureRequest
{
    public string Reason { get; init; } = string.Empty;
}

internal static class OtClientProcedureMapper
{
    public static OtClientProcedureResponse ToResponse(Domain.OtClientProcedures.OtClientProcedure procedure) =>
        new()
        {
            Id = procedure.Id,
            ClientTenantId = procedure.ClientTenantId,
            ProcedureTypeId = procedure.ProcedureTypeId,
            ProcedureTypeName = procedure.ProcedureTypeName,
            ClientTenantName = procedure.ClientTenantName,
            ReferenceNumber = procedure.ReferenceNumber,
            Status = procedure.Status,
            PlateFlowStatus = procedure.PlateFlowStatus,
            TransitOfficeId = procedure.TransitOfficeId,
            CreatedAt = procedure.CreatedAt,
            SubmittedAt = procedure.SubmittedAt,
            Prioritario = procedure.Prioritario,
        };
}
