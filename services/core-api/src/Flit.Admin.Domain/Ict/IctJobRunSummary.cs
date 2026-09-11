namespace Flit.Admin.Domain.Ict;

/// <summary>
/// Resumen de una corrida de pipeline. Sin <c>error_message</c>, payloads ni PII.
/// </summary>
public sealed record IctJobRunSummary(
    string JobName,
    DateTimeOffset StartedAt,
    int DurationMs,
    string Outcome);
