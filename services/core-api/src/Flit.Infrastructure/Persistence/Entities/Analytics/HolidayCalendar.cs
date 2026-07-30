namespace Flit.Infrastructure.Persistence.Entities.Analytics;

/// <summary>
/// Catálogo mixto de días festivos — <c>analytics.holiday_calendar</c> (Feature #11076).
/// <list type="bullet">
///   <item><c>TenantId IS NULL</c> → entrada global/compartida (festivos nacionales CO).</item>
///   <item><c>TenantId IS NOT NULL</c> → entrada específica de tenant (festivos regionales o laborales propios).</item>
/// </list>
/// RLS expone filas globales (NULL) y del tenant activo.
/// Referenciado por <c>report_sla_config.calendar_type = 'business'</c>.
/// </summary>
public sealed class HolidayCalendar
{
    public Guid Id { get; set; }

    /// <summary>
    /// NULL = entrada global (visible para todos los tenants).
    /// NOT NULL = entrada propia del tenant (festivos regionales o laborales personalizados).
    /// </summary>
    public Guid? TenantId { get; set; }

    public DateOnly HolidayDate { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Código ISO 3166-1 alpha-2. Default: CO (Colombia).</summary>
    public string CountryCode { get; set; } = "CO";

    public bool IsActive { get; set; } = true;

    /// <summary>Para integración con fuentes oficiales (DIVIPOLA, RUNT, etc.).</summary>
    public string ExternalRefs { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
