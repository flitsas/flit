namespace Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;

public sealed class GetOtClientProcedureQuery
{
    public Guid OtTenantId { get; init; }

    public Guid ProcedureInstanceId { get; init; }
}

public enum GetOtClientProcedureStatus
{
    Found,
    NotFound,
}

public sealed class GetOtClientProcedureResult
{
    public GetOtClientProcedureStatus Status { get; init; }

    public OtClientProcedureResponse? Procedure { get; init; }

    public static GetOtClientProcedureResult Found(OtClientProcedureResponse procedure) =>
        new() { Status = GetOtClientProcedureStatus.Found, Procedure = procedure };

    public static GetOtClientProcedureResult NotFound() =>
        new() { Status = GetOtClientProcedureStatus.NotFound };
}
