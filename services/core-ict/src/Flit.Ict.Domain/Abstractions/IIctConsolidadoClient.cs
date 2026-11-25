namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Consolidado entregado con su contexto: la URL prefirmada, el estado del trámite en core-api y si es
/// definitivo (trámite aprobado) o una versión preliminar.
/// </summary>
public sealed record ConsolidadoEntregado(string Url, string? Estado, bool Definitivo);

/// <summary>
/// Obtiene la URL prefirmada del PDF consolidado de un trámite materializado (HU #14668).
/// La implementación pide a core-api <c>DeliverConsolidado</c> (regenera on-demand si falta o
/// está stale) y luego solicita al File Manager una presigned GET URL con TTL de 600 s.
/// </summary>
public interface IIctConsolidadoClient
{
    /// <summary>
    /// Devuelve la URL prefirmada del consolidado (<c>tipo=consolidado</c>) del trámite indicado.
    /// </summary>
    /// <param name="tenantId">Tenant del pre-trámite.</param>
    /// <param name="procedureInstanceId">ID del trámite en <c>tramites.procedure_instances</c>.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>
    /// <c>(url, null)</c> cuando el consolidado se entregó y la URL se generó.<br/>
    /// <c>(null, "consolidado_no_disponible")</c> si no se pudo generar/entregar el PDF.<br/>
    /// <c>(null, "not_approved")</c> si el trámite no está aprobado (defensa en profundidad).<br/>
    /// <c>(null, "storage_unavailable")</c> si gRPC o File Manager no respondieron.
    /// </returns>
    Task<(string? Url, string? Error)> GetPresignedUrlAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        CancellationToken ct = default);

    /// <summary>
    /// Como <see cref="GetPresignedUrlAsync"/>, pero con <paramref name="preliminar"/> también entrega el
    /// consolidado vigente de un trámite que aún no está aprobado (tal cual, sin regenerarlo) y devuelve
    /// el estado del trámite y si la versión es definitiva.
    /// </summary>
    /// <returns>
    /// <c>(entrega, null)</c> cuando todo OK; si no, el código de error de core-api
    /// (<c>not_approved</c>, <c>anulado</c>, <c>consolidado_no_disponible</c>, …) o
    /// <c>storage_unavailable</c>.
    /// </returns>
    Task<(ConsolidadoEntregado? Entrega, string? Error)> GetConsolidadoAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        bool preliminar,
        CancellationToken ct = default);
}
