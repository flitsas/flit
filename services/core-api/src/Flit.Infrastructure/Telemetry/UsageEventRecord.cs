namespace Flit.Infrastructure.Telemetry;

/// <summary>
/// Evento de uso pendiente de persistir (cola en memoria → writer asíncrono).
/// Inmutable y sin PII en <see cref="MetadataJson"/> (contrato Reportes 2.0 §7).
/// </summary>
public sealed record UsageEventRecord(
    Guid TenantId,
    Guid? UserId,
    string EventType,
    string? Module,
    string? StepKey,
    Guid? ProcedureInstanceId,
    int? DurationMs,
    string MetadataJson,
    DateTimeOffset OccurredAt);
