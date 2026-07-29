using Flit.Admin.Domain.Companies.LegalRepresentatives;

namespace Flit.Admin.Application.Companies.Deeds.GetDeed;

/// <summary>
/// Obtiene una escritura por id dentro del tenant y adjunta una presigned URL de visualización inline
/// del PDF (vida corta). <c>null</c> si la escritura no existe. La URL de vista se resuelve contra el
/// storage y puede ser <c>null</c> si el binario no está disponible (el metadato existe pero el PDF
/// se perdió o aún no se subió).
/// </summary>
public sealed class GetDeedByIdHandler
{
    private readonly IDeedReader _reader;
    private readonly IDeedDocumentStorage _storage;

    public GetDeedByIdHandler(IDeedReader reader, IDeedDocumentStorage storage)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<DeedDetailResponse?> HandleAsync(
        GetDeedByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = await _reader
            .GetByIdAsync(query.TenantId, query.Id, cancellationToken).ConfigureAwait(false);

        if (item is null)
        {
            return null;
        }

        var view = string.IsNullOrWhiteSpace(item.StoragePath)
            ? null
            : await _storage.GetViewUrlAsync(item.StoragePath, cancellationToken).ConfigureAwait(false);

        return new DeedDetailResponse(DeedResponse.From(item), view?.Url, view?.ExpiresAt);
    }
}
