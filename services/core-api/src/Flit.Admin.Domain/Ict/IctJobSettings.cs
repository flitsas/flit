namespace Flit.Admin.Domain.Ict;

/// <summary>
/// Snapshot de <c>ict.job_settings</c> (fila única id=1). Configuración GLOBAL de plataforma:
/// sin tenant y sin secretos. Los jobs de core-ict la releen en caliente.
/// </summary>
public sealed record IctJobSettings(
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
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy)
{
    public static IctJobSettings Defaults() => new(
        WindowStartHour: 8,
        WindowEndHour: 20,
        BusinessPollSeconds: 45,
        ExternalPollSeconds: 45,
        OrchestratorPollSeconds: 20,
        OrchestratorConcurrency: 10,
        OrchestratorBatchSize: 50,
        SendPollSeconds: 20,
        SendConcurrency: 5,
        SendBatchSize: 50,
        WebhookPollSeconds: 10,
        WebhookBatchSize: 50,
        BusinessBatchSize: 500,
        ExternalBatchSize: 500,
        UpdatedAt: default,
        UpdatedBy: null);
}
