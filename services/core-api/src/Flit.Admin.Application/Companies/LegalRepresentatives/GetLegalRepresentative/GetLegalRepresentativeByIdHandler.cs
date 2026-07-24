using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.LegalRepresentatives.GetLegalRepresentative;

/// <summary>Obtiene un representante legal por id dentro del tenant; <c>null</c> si no existe (HU #10901).</summary>
public sealed class GetLegalRepresentativeByIdHandler
{
    private readonly ILegalRepresentativeReader _reader;

    public GetLegalRepresentativeByIdHandler(ILegalRepresentativeReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public async Task<LegalRepresentativeResponse?> HandleAsync(
        GetLegalRepresentativeByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = await _reader
            .GetByIdAsync(query.TenantId, query.Id, cancellationToken).ConfigureAwait(false);

        return item is null ? null : LegalRepresentativeResponse.From(item);
    }
}
