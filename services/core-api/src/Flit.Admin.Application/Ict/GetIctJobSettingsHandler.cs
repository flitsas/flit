using Flit.Admin.Domain.Ict;

namespace Flit.Admin.Application.Ict;

/// <summary>Lee el singleton <c>ict.job_settings</c> (HU #12512 AC1).</summary>
public sealed class GetIctJobSettingsHandler
{
    private readonly IIctJobSettingsRepository _repository;

    public GetIctJobSettingsHandler(IIctJobSettingsRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IctJobSettingsView> HandleAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
        return IctJobSettingsMapper.ToView(settings);
    }
}
