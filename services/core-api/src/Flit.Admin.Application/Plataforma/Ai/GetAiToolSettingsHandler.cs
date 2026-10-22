using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

public sealed class GetAiToolSettingsHandler
{
    private readonly IAiToolProviderSettingsRepository _repository;
    private readonly IAiApiKeyEnvironmentProbe _keyProbe;

    public GetAiToolSettingsHandler(
        IAiToolProviderSettingsRepository repository,
        IAiApiKeyEnvironmentProbe keyProbe)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyProbe = keyProbe ?? throw new ArgumentNullException(nameof(keyProbe));
    }

    public async Task<AiToolSettingsView?> HandleAsync(
        string toolCode,
        CancellationToken cancellationToken = default)
    {
        if (!AiToolCodes.IsKnown(toolCode))
        {
            return null;
        }

        var normalized = AiToolCodes.Normalize(toolCode);
        var row = await _repository.GetByToolCodeAsync(normalized, cancellationToken).ConfigureAwait(false);
        return row is null ? null : AiToolSettingsMapper.ToView(row, _keyProbe);
    }
}
