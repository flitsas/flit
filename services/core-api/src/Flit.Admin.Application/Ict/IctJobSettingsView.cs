namespace Flit.Admin.Application.Ict;

/// <summary>Vista SuperAdmin de <c>ict.job_settings</c>. Sin secretos ni connection strings.</summary>
public sealed record IctJobSettingsView(
    int WindowStartHour,
    int WindowEndHour,
    int BusinessPollSeconds,
    int ExternalPollSeconds,
    int OrchestratorPollSeconds,
    int OrchestratorConcurrency,
    int OrchestratorBatchSize,
    int SendPollSeconds,
    int SendConcurrency,
    int SendBatchSize,
    int WebhookPollSeconds,
    int WebhookBatchSize,
    int BusinessBatchSize,
    int ExternalBatchSize,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy);
