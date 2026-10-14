using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

public sealed class ListAiToolsHandler
{
    private readonly IAiToolProviderSettingsRepository _repository;
    private readonly IAiApiKeyEnvironmentProbe _keyProbe;

    public ListAiToolsHandler(
        IAiToolProviderSettingsRepository repository,
        IAiApiKeyEnvironmentProbe keyProbe)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyProbe = keyProbe ?? throw new ArgumentNullException(nameof(keyProbe));
    }

    public async Task<AiToolsListView> HandleAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var views = rows.Select(r => AiToolSettingsMapper.ToView(r, _keyProbe)).ToList();
        return new AiToolsListView(views);
    }
}
