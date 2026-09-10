using Flit.Analytics.Application.Abstractions;

namespace Flit.Analytics.Application.Queries.Metrics;

// ── Filtros y comparación (Reportes 2.0 §4.1) ─────────────────────────────────────────────────────

/// <summary>
/// Filtros comunes de los endpoints de métricas (Reportes 2.0, §4.1). El tenant SIEMPRE viene
/// resuelto (no hay vista global en estos endpoints). <see cref="StuckDays"/> aplica solo a
/// ot-metrics y live-overview (default 7, rango 1..90 — validado en el handler).
/// </summary>
public sealed record MetricsFilter(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    Guid? TransitOfficeId = null,
    Guid? ProcedureTypeId = null,
    Guid? OperatorUserId = null,
    string? Status = null,
    string? Reason = null,
    int StuckDays = 7);

/// <summary>Ventana de comparación aplicada (<c>previous_period</c> | <c>previous_year</c>).</summary>
public sealed record ComparisonInfoDto(string Mode, DateOnly From, DateOnly To);

/// <summary>
/// Envoltura de respuesta comparada (§4.1): <c>previous</c>/<c>comparison</c> son null cuando no se
/// pidió <c>compareWith</c>. El backend NO calcula deltas (los calcula el frontend).
/// </summary>
public sealed record ComparedResponse<T>(T Current, T? Previous, ComparisonInfoDto? Comparison)
    where T : class;

// ── OT metrics (§4.2) ─────────────────────────────────────────────────────────────────────────────

/// <summary>KPIs de cabecera de la pestaña Organismo de Tránsito.</summary>
public sealed record OtMetricsSummaryDto(
    int Entregados,
    int Aprobados,
    int Rechazados,
    double RejectionRatePct,
    double? AvgApprovalHours,
    double? P50ApprovalHours,
    double? P90ApprovalHours,
    double ReincidencePct,
    int StuckCount);

/// <summary>Tasa de rechazo por Organismo de Tránsito.</summary>
public sealed record RejectionByOfficeDto(
    Guid TransitOfficeId,
    string TransitOfficeName,
    int Entregados,
    int Aprobados,
    int Rechazados,
    double RejectionRatePct);

/// <summary>
/// Distribución de causales de rechazo escritas a mano (<c>reason</c>). Se conserva porque los
/// rechazos anteriores al catálogo solo tienen texto libre, pero NO es agregable: cada revisor
/// escribe distinto y devuelve decenas de motivos con un caso cada uno. La lectura útil es
/// <see cref="RejectionByReasonCatalogDto"/>.
/// </summary>
public sealed record RejectionByReasonDto(string Reason, int Count, double Pct);

/// <summary>
/// Causal TIPIFICADA del catálogo global.
/// <para><see cref="Pct"/> es el porcentaje de RECHAZOS que incluyen esta causal, no el reparto de
/// un total: un rechazo puede llevar varias causales, así que la suma puede pasar del 100 %. Es una
/// distinción que hay que respetar al pintarlo, porque leído como reparto el número engaña.</para>
/// </summary>
public sealed record RejectionByReasonCatalogDto(
    Guid ReasonId,
    string Code,
    string Description,
    int Rechazos,
    double Pct);

/// <summary>
/// Tramo PROPIO del ciclo: horas desde que se crea el trámite hasta que se entrega por primera vez.
/// Complementa a <see cref="ApprovalTimesByOfficeDto"/> (el tramo del organismo) y cierra la
/// partición del tiempo: hasta ahora se medía al organismo pero no a uno mismo.
/// </summary>
public sealed record InternalCycleDto(double? AvgHours, double? P50Hours, double? P90Hours);

/// <summary>Tasa de rechazo por tipo de trámite.</summary>
public sealed record RejectionByTypeDto(
    Guid ProcedureTypeId,
    string ProcedureTypeName,
    int Entregados,
    int Rechazados,
    double RejectionRatePct);

/// <summary>Tiempos entregado→aprobado|rechazado por OT (avg/p50/p90 en horas).</summary>
public sealed record ApprovalTimesByOfficeDto(
    Guid TransitOfficeId,
    string TransitOfficeName,
    int Decididos,
    double? AvgHours,
    double? P50Hours,
    double? P90Hours);

/// <summary>Ranking de agilidad de OTs (p50 asc, desempate por tasa de rechazo asc).</summary>
public sealed record OfficeRankingDto(
    Guid TransitOfficeId,
    string TransitOfficeName,
    int Rank,
    double P50Hours,
    double RejectionRatePct,
    int Volumen);

/// <summary>Reincidencia: rechazados que volvieron a borrador y ciclos por rechazado.</summary>
public sealed record ReincidenceDto(int Rechazadas, int Reintentadas, double AvgCiclos, int MaxCiclos);

/// <summary>Instancia atascada (sin cambio de estado hace más de <c>stuckDays</c> días).</summary>
public sealed record StuckItemDto(
    Guid InstanceId,
    string ReferenceNumber,
    string Status,
    double DaysInStatus,
    string? TransitOfficeName,
    string ProcedureTypeName,
    string CreatedByDisplayName);

/// <summary>Atascados: total del universo filtrado + top 50 por días en el estado.</summary>
public sealed record StuckDto(int TotalCount, IReadOnlyList<StuckItemDto> Items);

/// <summary>Cuerpo de <c>GET /analytics/ot-metrics</c> (§4.2).</summary>
public sealed record OtMetricsDto(
    OtMetricsSummaryDto Summary,
    IReadOnlyList<RejectionByOfficeDto> RejectionByOffice,
    IReadOnlyList<RejectionByReasonDto> RejectionByReason,
    IReadOnlyList<RejectionByTypeDto> RejectionByType,
    IReadOnlyList<ApprovalTimesByOfficeDto> ApprovalTimesByOffice,
    IReadOnlyList<OfficeRankingDto> OfficeRanking,
    ReincidenceDto Reincidence,
    StuckDto Stuck,
    /// <summary>Causales del catálogo. Vacío mientras no haya rechazos tipificados en el rango.</summary>
    IReadOnlyList<RejectionByReasonCatalogDto> RejectionByReasonCatalog,
    /// <summary>
    /// Causales marcadas por rechazo (promedio). Es el indicador de salud del dato: si se acerca al
    /// tamaño del catálogo, alguien está marcando todo y la distribución deja de discriminar.
    /// </summary>
    double AvgReasonsPerRejection,
    InternalCycleDto InternalCycle);

// ── Funnel (§4.3) ─────────────────────────────────────────────────────────────────────────────────

/// <summary>Etapa del funnel de estados (instancias distintas que la alcanzaron).</summary>
public sealed record FunnelStageDto(string Stage, int Count, double PctOfFirst, double PctOfPrev);

/// <summary>
/// Parte del funnel que sale de las tablas operacionales (sin telemetría): el handler la
/// compone con <see cref="WizardStepMetricDto"/> de <c>IUsageMetricsReadRepository</c> (HU-A).
/// </summary>
public sealed record FunnelCoreDto(
    IReadOnlyList<FunnelStageDto> States,
    int Anulados,
    int RechazadosVigentes);

/// <summary>Cuerpo de <c>GET /analytics/funnel</c> (§4.3).</summary>
public sealed record FunnelDto(
    IReadOnlyList<FunnelStageDto> States,
    int Anulados,
    int RechazadosVigentes,
    IReadOnlyList<WizardStepMetricDto> WizardSteps);

// ── Usage (§4.4) ──────────────────────────────────────────────────────────────────────────────────

/// <summary>Reemplazos de documentos por tipo (regla §0: filas extra por (instancia, tipo)).</summary>
public sealed record DocumentReplacementDto(string DocumentTipo, int Uploads, int Replacements);

/// <summary>Métricas de integraciones externas desde <c>admin.ot_api_call_logs</c>.</summary>
public sealed record ExternalApiMetricDto(
    string Endpoint,
    string Direction,
    int Calls,
    int Errors,
    double ErrorRatePct,
    double? AvgDurationMs,
    double? P90DurationMs);

/// <summary>Cuerpo de <c>GET /analytics/usage</c> (§4.4).</summary>
public sealed record UsageDto(
    IReadOnlyList<ModuleUsageDto> ModuleUsage,
    IReadOnlyList<WizardStepMetricDto> WizardSteps,
    IReadOnlyList<PeakHourDto> PeakHours,
    IReadOnlyList<DocumentReplacementDto> DocumentReplacements,
    IReadOnlyList<ExternalApiMetricDto> ExternalApis,
    double? AvgWizardDurationMs,
    double? MedianWizardDurationMs);

// ── Live overview (§4.5) ──────────────────────────────────────────────────────────────────────────

/// <summary>Conteo por estado actual (instancias activas del tenant).</summary>
public sealed record StatusCountItemDto(string Status, int Count);

/// <summary>Actividad de HOY (día calendario America/Bogota).</summary>
public sealed record LiveTodayDto(
    int Creados,
    IReadOnlyList<StatusCountItemDto> ByStatus,
    int Entregados,
    int Aprobados,
    int Rechazados);

/// <summary>Integraciones externas de la última hora.</summary>
public sealed record IntegrationsLastHourDto(int Calls, int Errors, double? AvgDurationMs);

/// <summary>
/// Datos del live-overview leídos en UNA sola ronda (batch multi-resultset); el handler les
/// agrega <c>generatedAt</c>.
/// </summary>
public sealed record LiveOverviewDataDto(
    LiveTodayDto Today,
    int StuckCount,
    int PendingIdentityValidations,
    IntegrationsLastHourDto IntegrationsLastHour,
    DateTimeOffset? LastActivityAt);

/// <summary>Cuerpo de <c>GET /analytics/live-overview</c> (§4.5).</summary>
public sealed record LiveOverviewDto(
    DateTimeOffset GeneratedAt,
    LiveTodayDto Today,
    int StuckCount,
    int PendingIdentityValidations,
    IntegrationsLastHourDto IntegrationsLastHour,
    DateTimeOffset? LastActivityAt);
