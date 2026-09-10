namespace Flit.Admin.Application.OtClientProcedures.GetOtClientProcedure;

public sealed class GetOtClientProcedureQuery
{
    public Guid OtTenantId { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    /// <summary>
    /// Organismo con el que el SuperAdmin supervisa la bandeja (ruta /admin/transit-offices/{id}).
    /// Nulo para el ot_admin, que se resuelve por su propio tenant.
    /// </summary>
    public Guid? TransitOfficeId { get; init; }
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
