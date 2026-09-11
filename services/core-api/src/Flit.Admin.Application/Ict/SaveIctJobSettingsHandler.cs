using Flit.Admin.Domain.Ict;

namespace Flit.Admin.Application.Ict;

/// <summary>Persiste clamps validados en <c>ict.job_settings</c> (HU #12512 AC2).</summary>
public sealed class SaveIctJobSettingsHandler
{
    private readonly IIctJobSettingsRepository _repository;

    public SaveIctJobSettingsHandler(IIctJobSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<SaveIctJobSettingsResult> HandleAsync(
        SaveIctJobSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var candidate = new IctJobSettings(
            command.WindowStartHour,
            command.WindowEndHour,
            command.BusinessPollSeconds,
            command.ExternalPollSeconds,
            command.OrchestratorPollSeconds,
            command.OrchestratorConcurrency,
            command.OrchestratorBatchSize,
            command.SendPollSeconds,
            command.SendConcurrency,
            command.SendBatchSize,
            command.WebhookPollSeconds,
            command.WebhookBatchSize,
            command.BusinessBatchSize,
            command.ExternalBatchSize,
            DateTimeOffset.UtcNow,
            command.UpdatedBy);

        var errors = IctJobSettingsRules.Validate(candidate);
        if (errors.Count > 0)
        {
            return SaveIctJobSettingsResult.Invalid(errors);
        }

        var saved = await _repository.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        return SaveIctJobSettingsResult.Ok(IctJobSettingsMapper.ToView(saved));
    }
}
