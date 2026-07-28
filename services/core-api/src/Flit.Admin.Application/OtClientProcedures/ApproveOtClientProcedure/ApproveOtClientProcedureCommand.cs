namespace Flit.Admin.Application.OtClientProcedures.ApproveOtClientProcedure;

public sealed class ApproveOtClientProcedureCommand
{
    public Guid OtTenantId { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    public Guid? ApprovedBy { get; init; }

    /// <summary>
    /// ADR-0036 §D9 (HU #10916) — mandatario resuelto para firmar el mandato (el endpoint lo resolvió/
    /// eligió antes de aprobar). <c>null</c> = el trámite no exige mandato-persona o es institucional.
    /// Se persiste en <c>procedure_instances.mandate_signer_id</c> en el mismo save que la aprobación.
    /// </summary>
    public Guid? MandateSignerId { get; init; }
}

public enum ApproveOtClientProcedureStatus
{
    Approved,
    NotFound,
    InvalidState,
    QuipuxReadOnly,
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

    public static ApproveOtClientProcedureResult QuipuxReadOnly() =>
        new() { Status = ApproveOtClientProcedureStatus.QuipuxReadOnly };
}
