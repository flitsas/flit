namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>
/// Origen de una transición de estado OT (approved_ot/rejected_ot), persistido en
/// <c>status_history.metadata.source</c> para trazabilidad y atribución en analítica/auditoría
/// (HU #10432). Permite distinguir una acción humana del organismo de una de sistema (integración).
/// </summary>
public static class OtTransitionSource
{
    /// <summary>Acción manual de un administrador del organismo de tránsito (changed_by = admin).</summary>
    public const string OtAdmin = "ot_admin";

    /// <summary>Callback inbound de la integración Quipux (changed_by null — acción de sistema).</summary>
    public const string QuipuxWebhook = "quipux_webhook";
}
