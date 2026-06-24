namespace Flit.Tramites.Domain.Enums;

public static class ProcedureInstanceStatus
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string InReview = "in_review";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    /// <summary>En cola de aprobación OT (HU #10217).</summary>
    public const string PendingOt = "pending_ot";

    /// <summary>Aprobado por OT tenant admin (HU #10217).</summary>
    public const string ApprovedOt = "approved_ot";

    /// <summary>Rechazado por OT tenant admin (HU #10217).</summary>
    public const string RejectedOt = "rejected_ot";
}
