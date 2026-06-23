namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Resumen de trámite de cliente visible para OT admin (HU #10217).</summary>
public sealed class OtClientProcedure
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
