namespace Flit.Ict.Domain.Abstractions;

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
}
