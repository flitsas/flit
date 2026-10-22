using System.Net;

namespace Flit.Admin.Domain.Integrations;

/// <summary>
/// HU #13086 (Feature #13067, Épica #12737) — una solicitud a <c>/api/v1/external/*</c> para la bitácora
/// <c>integrations.external_access_log</c> (Ley 1581). Sin cuerpos, datos personales, secretos ni pases.
/// </summary>
/// <param name="ClientId">Cliente del pase validado; en el canje del pase, el solicitado. <c>null</c> sin pase válido.</param>
/// <param name="Endpoint"><c>token</c>, <c>tramites.sync</c>, <c>tramites.adjunto-url</c> u <c>otro</c>.</param>
/// <param name="SyncVersionFrom"><c>syncVersion</c> del primer ítem entregado (solo el feed, con ítems).</param>
/// <param name="SyncVersionTo"><c>syncVersion</c> del último ítem entregado (solo el feed, con ítems).</param>
/// <param name="TenantIds">Compañías de los trámites entregados, sin repetir (solo el feed).</param>
/// <param name="PiiUnmasked"><c>true</c> si la respuesta llevó datos personales de compradores en claro.</param>
public sealed record ExternalAccessLogEntry(
    string? ClientId,
    string Endpoint,
    string? RequestId,
    IPAddress? Ip,
    long? SyncVersionFrom,
    long? SyncVersionTo,
    int? ItemsCount,
    IReadOnlyList<Guid>? TenantIds,
    bool PiiUnmasked,
    int HttpStatus,
    int DurationMs,
    DateTimeOffset OccurredAt);

public interface IExternalAccessLogRepository
{
    Task AddAsync(ExternalAccessLogEntry entry, CancellationToken cancellationToken = default);
}
