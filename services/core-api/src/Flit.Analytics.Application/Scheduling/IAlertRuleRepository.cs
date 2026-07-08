namespace Flit.Analytics.Application.Scheduling;

/// <summary>
/// Persistencia de reglas de alerta y lectura del historial de disparos
/// (<c>analytics.alert_rules</c> / <c>analytics.alert_events</c>, HU-D Reportes 2.0).
/// Implementada en Flit.Infrastructure con EF Core. TODAS las operaciones filtran por tenant;
/// borrado lógico con <c>deleted_at</c>.
/// </summary>
public interface IAlertRuleRepository
{
    Task<IReadOnlyList<AlertRuleDto>> ListAsync(Guid tenantId, CancellationToken ct);

    Task<AlertRuleDto> CreateAsync(
        Guid tenantId, Guid? createdBy, ValidatedAlertRule data, CancellationToken ct);

    /// <summary>Null si la regla no existe (o pertenece a otro tenant / está eliminada).</summary>
    Task<AlertRuleDto?> UpdateAsync(
        Guid tenantId, Guid id, ValidatedAlertRule data, CancellationToken ct);

    /// <summary>False si la regla no existe (o pertenece a otro tenant / ya está eliminada).</summary>
    Task<bool> SoftDeleteAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>Historial de disparos paginado, más recientes primero; filtrable por regla.</summary>
    Task<AlertEventsPageDto> ListEventsAsync(
        Guid tenantId, Guid? ruleId, int page, int pageSize, CancellationToken ct);
}
