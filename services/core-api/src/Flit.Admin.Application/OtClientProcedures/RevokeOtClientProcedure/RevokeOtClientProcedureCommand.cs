namespace Flit.Admin.Application.OtClientProcedures.RevokeOtClientProcedure;

public sealed class RevokeOtClientProcedureCommand
{
    public Guid OtTenantId { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    public Guid? RevokedBy { get; init; }

    /// <summary>Motivo opcional de la revocación (auditoría). No exigido por ningún AC de HU #12166.</summary>
    public string? Reason { get; init; }

    /// <summary>Override de organismo para SuperAdmin (mismo contrato que approve/reject).</summary>
    public Guid? TransitOfficeId { get; init; }
}

public enum RevokeOtClientProcedureStatus
{
    Revoked,
    NotFound,
    InvalidState,
    QuipuxReadOnly,
}

public sealed class RevokeOtClientProcedureResult
{
    public RevokeOtClientProcedureStatus Status { get; init; }

    public OtClientProcedureResponse? Procedure { get; init; }

    public static RevokeOtClientProcedureResult Revoked(OtClientProcedureResponse procedure) =>
        new() { Status = RevokeOtClientProcedureStatus.Revoked, Procedure = procedure };

    public static RevokeOtClientProcedureResult NotFound() =>
        new() { Status = RevokeOtClientProcedureStatus.NotFound };

    public static RevokeOtClientProcedureResult InvalidState() =>
        new() { Status = RevokeOtClientProcedureStatus.InvalidState };

    public static RevokeOtClientProcedureResult QuipuxReadOnly() =>
        new() { Status = RevokeOtClientProcedureStatus.QuipuxReadOnly };
}
