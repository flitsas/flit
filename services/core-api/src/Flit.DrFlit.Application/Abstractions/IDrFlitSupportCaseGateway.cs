using Flit.DrFlit.Application.SupportCases;

namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Puerto hacia el sistema de soporte donde se radican los casos de DR. FLIT (HU #12923, ADR-0060 §7.2).
/// Hoy es un Bug en Azure DevOps (proyecto <c>FLIT - SOPORTE</c>). El mapeo de prioridad, frecuencia,
/// ambiente y módulo a los campos del destino vive en la implementación y es configurable.
/// </summary>
public interface IDrFlitSupportCaseGateway
{
    /// <summary>
    /// Sube los adjuntos (un fallo en uno no bloquea: se excluye y se cuenta) y crea el caso con los que
    /// sí subieron. Nunca lanza por fallos del proveedor: los reporta en el resultado.
    /// </summary>
    Task<DrFlitBugCreationResult> CreateBugAsync(
        DrFlitSupportTicket ticket,
        IReadOnlyList<DrFlitTicketAttachment> attachments,
        CancellationToken ct);
}
