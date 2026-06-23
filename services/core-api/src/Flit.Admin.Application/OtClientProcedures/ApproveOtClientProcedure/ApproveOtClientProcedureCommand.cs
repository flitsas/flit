namespace Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;

public sealed class ApproveOtClientProcedureCommand
{
    public Guid OtTenantId { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    public Guid? ApprovedBy { get; init; }
}

public enum ApproveOtClientProcedureStatus
{
    Approved,
    NotFound,
    InvalidState,
}

public sealed class ApproveOtClientProcedureResult
{
    public ApproveOtClientProcedureStatus Status { get; init; }

    public OtClientProcedureResponse? Procedure { get; init; }

    public static ApproveOtClientProcedureResult Approved(OtClientProcedureResponse procedure) =>
        new() { Status = ApproveOtClientProcedureStatus.Approved, Procedure = procedure };

    public static ApproveOtClientProcedureResult NotFound() =>
        new() { Status = ApproveOtClientProcedureStatus.NotFound };

    public static ApproveOtClientProcedureResult InvalidState() =>
        new() { Status = ApproveOtClientProcedureStatus.InvalidState };
}
