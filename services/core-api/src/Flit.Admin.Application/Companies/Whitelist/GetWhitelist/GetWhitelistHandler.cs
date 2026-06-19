using Flit.Admin.Domain.Companies.Whitelist;

namespace Flit.Admin.Application.Companies.Whitelist.GetWhitelist;

/// <summary>
/// Caso de uso de lectura de la lista blanca del tenant (AC6). Devuelve los correos
/// activos; lista vacía si no hay ninguno.
/// </summary>
public sealed class GetWhitelistHandler
{
    private readonly IWhitelistRepository _repository;

    public GetWhitelistHandler(IWhitelistRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<WhitelistEntryResponse>> HandleAsync(
        GetWhitelistQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entries = await _repository.ListAsync(query.TenantId, cancellationToken).ConfigureAwait(false);

        return [.. entries.Select(e => new WhitelistEntryResponse(e.Email, e.CreatedAt, e.AddedBy))];
    }
}
