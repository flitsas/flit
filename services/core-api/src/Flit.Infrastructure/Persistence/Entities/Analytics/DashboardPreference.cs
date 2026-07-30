namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Preferencias de dashboard KPI por usuario — <c>analytics.dashboard_preferences</c>.
/// (Feature #11076.) Una fila por (tenant_id, user_id). Sin constructor de consultas libre;
/// solo mostrar/ocultar/reordenar los KPIs predefinidos.
/// </summary>
public sealed class DashboardPreference
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// Configuración JSON: <c>{ "visibleKpis": [...], "kpiOrder": [...], "hiddenCharts": [...] }</c>.
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    // ── Columnas estándar A5 ──────────────────────────────────────────────
    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
