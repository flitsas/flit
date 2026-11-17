namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Alcance de compañías cliente del USUARIO en curso dentro de un organismo de tránsito (OT).
///
/// <para>Es la única definición de «qué compañías cliente puede ver este usuario del OT». La consumen los
/// puntos centrales de visibilidad (bandeja, contadores, detalle y acciones, métricas, consultas, reportes del
/// portal, lotes de consolidados, mandatarios) para intersecar su universo, de modo que no queden copias
/// desincronizadas de la regla.</para>
///
/// <para>Contrato: <c>null</c> = sin restricción (modo <c>todos</c>, ausencia de fila, SuperAdmin actuando sobre el
/// organismo, procesos sin usuario HTTP como integraciones por API key o jobs). Una lista = solo esas compañías
/// (puede venir vacía si el alcance quedó huérfano: ve nada, falla cerrado).</para>
/// </summary>
public interface IOtUserClientScopeResolver
{
    /// <summary>
    /// Compañías permitidas al usuario en curso en <paramref name="transitOfficeId"/>, o <c>null</c> si no hay
    /// restricción.
    /// </summary>
    Task<IReadOnlyList<Guid>?> GetRestrictionAsync(Guid transitOfficeId, CancellationToken cancellationToken = default);
}
