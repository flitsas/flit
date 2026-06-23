namespace Flit.Admin.Application.OtClientProcedures.RejectOtClientProcedure;

public sealed class RejectOtClientProcedureCommand
{
    public Guid OtTenantId { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    public Guid? RejectedBy { get; init; }

    public RejectOtClientProcedureRequest Request { get; init; } = new();
}

public enum RejectOtClientProcedureStatus
{
    Rejected,
    NotFound,
    InvalidState,
    ValidationFailed,
}

public sealed class RejectOtClientProcedureResult
{
    public RejectOtClientProcedureStatus Status { get; init; }

    public OtClientProcedureResponse? Procedure { get; init; }

    public IReadOnlyList<FieldError> Errors { get; init; } = [];

    public static RejectOtClientProcedureResult Rejected(OtClientProcedureResponse procedure) =>
        new() { Status = RejectOtClientProcedureStatus.Rejected, Procedure = procedure };

    public static RejectOtClientProcedureResult NotFound() =>
        new() { Status = RejectOtClientProcedureStatus.NotFound };

    public static RejectOtClientProcedureResult InvalidState() =>
        new() { Status = RejectOtClientProcedureStatus.InvalidState };

    public static RejectOtClientProcedureResult ValidationFailed(params FieldError[] errors) =>
        new() { Status = RejectOtClientProcedureStatus.ValidationFailed, Errors = errors };
}

public sealed record FieldError(string Field, string Message);
