namespace Flit.Admin.Application.Ict;

public sealed record SaveIctJobSettingsCommand(
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
    Guid? UpdatedBy);
