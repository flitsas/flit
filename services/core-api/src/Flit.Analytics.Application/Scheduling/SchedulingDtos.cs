namespace Flit.Analytics.Application.Scheduling;

// Contratos de Reportes 2.0 — HU-D (§4.7 de docs/contratos-reportes-v2.md).
// Los nombres de campo son contrato con el frontend: NO renombrar sin coordinar.

/// <summary>Informe programado (tabla <c>analytics.report_schedules</c>).</summary>
public sealed record ReportScheduleDto(
    Guid Id,
    string Name,
    string ReportType,
    string Frequency,
    int? DayOfWeek,
    int? DayOfMonth,
    int SendHour,
    string Format,
    IReadOnlyList<string> Recipients,
    bool IsActive,
    DateTimeOffset? LastSentAt);

/// <summary>Regla de alerta por umbral (tabla <c>analytics.alert_rules</c>).</summary>
public sealed record AlertRuleDto(
    Guid Id,
    string Name,
    string Metric,
    string Operator,
    decimal Threshold,
    int WindowMinutes,
    int CooldownMinutes,
    IReadOnlyList<string> Recipients,
    bool IsActive,
    DateTimeOffset? LastTriggeredAt);

/// <summary>Disparo registrado de una alerta (tabla <c>analytics.alert_events</c>).</summary>
public sealed record AlertEventDto(
    Guid Id,
    Guid AlertRuleId,
    string RuleName,
    DateTimeOffset TriggeredAt,
    decimal MetricValue,
    decimal Threshold,
    bool Notified,
    string? Message,
    DateTimeOffset? AcknowledgedAt,
    Guid? AcknowledgedBy);

/// <summary>Página del historial de disparos (<c>GET /analytics/alert-events</c>).</summary>
public sealed record AlertEventsPageDto(IReadOnlyList<AlertEventDto> Items, int TotalCount);

/// <summary>
/// Payload crudo de creación/edición de un informe programado (POST/PUT). Todos los campos
/// nullables para que la validación (<see cref="SchedulingValidation"/>) produzca mensajes
/// en español en lugar de fallos de binding.
/// </summary>
public sealed record ReportScheduleInput(
    string? Name,
    string? ReportType,
    string? Frequency,
    int? DayOfWeek,
    int? DayOfMonth,
    int? SendHour,
    string? Format,
    IReadOnlyList<string>? Recipients,
    bool? IsActive);

/// <summary>Payload crudo de creación/edición de una regla de alerta (POST/PUT).</summary>
public sealed record AlertRuleInput(
    string? Name,
    string? Metric,
    string? Operator,
    decimal? Threshold,
    int? WindowMinutes,
    int? CooldownMinutes,
    IReadOnlyList<string>? Recipients,
    bool? IsActive);

/// <summary>Informe programado ya validado y normalizado (defaults aplicados).</summary>
public sealed record ValidatedReportSchedule(
    string Name,
    string ReportType,
    string Frequency,
    int? DayOfWeek,
    int? DayOfMonth,
    int SendHour,
    string Format,
    IReadOnlyList<string> Recipients,
    bool IsActive);

/// <summary>Regla de alerta ya validada y normalizada (defaults aplicados).</summary>
public sealed record ValidatedAlertRule(
    string Name,
    string Metric,
    string Operator,
    decimal Threshold,
    int WindowMinutes,
    int CooldownMinutes,
    IReadOnlyList<string> Recipients,
    bool IsActive);
