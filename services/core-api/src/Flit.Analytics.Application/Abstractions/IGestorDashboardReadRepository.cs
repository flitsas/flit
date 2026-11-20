using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Abstractions;

/// <summary>
/// Lecturas del Inicio del Gestor (rediseño de los 3 dashboards). Siempre acotadas a UN tenant —no hay vista global—
/// con el mismo patrón que <see cref="IAnalyticsReadRepository"/>: GUC <c>app.current_tenant_id</c> + filtro explícito
/// por <c>tenant_id</c>. <c>families</c>: códigos de familia (<c>MATRICULAS</c>, <c>TRASPASO</c>, <c>OTROS</c>);
/// <c>null</c> = sin restricción, vacío = ninguna familia (ningún trámite).
/// </summary>
public interface IGestorDashboardReadRepository
{
    /// <summary>
    /// Trámites del tenant ordenados por días sin movimiento (descendente), hasta <paramref name="limit"/>.
    /// <paramref name="estados"/> son los estados admitidos (nunca vacío).
    /// </summary>
    Task<GestorDashboardColaDto> GetColaInactividadAsync(
        Guid tenantId, IReadOnlyList<string> estados, IReadOnlyList<string>? families, int limit,
        CancellationToken ct = default);
}
