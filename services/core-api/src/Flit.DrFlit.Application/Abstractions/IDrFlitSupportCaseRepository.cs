using Flit.DrFlit.Application.SupportCases;

namespace Flit.DrFlit.Application.Abstractions;

/// <summary>
/// Registro de los intentos de radicar un caso (<c>dr_flit.support_cases</c>, HU #12925). La fila se
/// escribe en <c>pending</c> ANTES de llamar al proveedor (ADR-0060 §7.2), así un caso nunca se pierde
/// sin rastro aunque el proceso muera a mitad.
/// </summary>
public interface IDrFlitSupportCaseRepository
{
    Task<Guid> InsertPendingAsync(DrFlitSupportCaseRecord record, CancellationToken ct);

    Task MarkCreatedAsync(Guid id, int workItemId, int attachmentCount, int attachmentFailures, CancellationToken ct);

    /// <param name="errorCode">Código del fallo sin PII (p. ej. <c>http_502</c>).</param>
    Task MarkFailedAsync(Guid id, string errorCode, int attachmentCount, int attachmentFailures, CancellationToken ct);
}

/// <summary>Lo que se persiste del caso: exactamente lo que se envía al proveedor.</summary>
public sealed record DrFlitSupportCaseRecord(
    Guid TenantId,
    Guid UserId,
    string AdoProject,
    DrFlitSupportTicket Ticket,
    string AffectedModule);
