namespace Flit.Admin.Application.OtClientProcedures;

public sealed class OtClientProcedureResponse
{
    public Guid Id { get; init; }

    public Guid ClientTenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public string ReferenceNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public Guid? TransitOfficeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }
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
            ReferenceNumber = procedure.ReferenceNumber,
            Status = procedure.Status,
            TransitOfficeId = procedure.TransitOfficeId,
            CreatedAt = procedure.CreatedAt,
            SubmittedAt = procedure.SubmittedAt,
        };
}
