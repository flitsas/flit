using Flit.Admin.Application.Banners.Ports;
using Flit.Admin.Domain.Banners;

namespace Flit.Admin.Application.Banners.GetBannerImage;

/// <summary>
/// Caso de uso del endpoint publico de imagen de banner (HU #12240, AC2/AC3). ETag = SHA-256
/// persistido (ADR-0057). Si <c>ifNoneMatch</c> coincide, NO abre el binario — evita releer el
/// storage en un HIT de cache (AC2, requisito explicito).
/// </summary>
public sealed class GetBannerImageHandler
{
    private readonly IBannerRepository _repository;
    private readonly IBannerImageStorage _storage;

    public GetBannerImageHandler(IBannerRepository repository, IBannerImageStorage storage)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<GetBannerImageResult> HandleAsync(
        Guid id,
        string? ifNoneMatch,
        CancellationToken cancellationToken = default)
    {
        var imageRef = await _repository.GetImageRefAsync(id, cancellationToken).ConfigureAwait(false);
        if (imageRef is null)
        {
            return GetBannerImageResult.NotFound();
        }

        var etag = $"\"{imageRef.Sha256}\"";
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && MatchesEtag(ifNoneMatch, etag))
        {
            return GetBannerImageResult.NotModified(imageRef.Sha256);
        }

        var stream = await _storage.OpenReadAsync(imageRef.StoragePath, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            // Fila en BD pero binario perdido en storage: mismo 404 que "no encontrado" (AC3),
            // sin distinguir el motivo al caller.
            return GetBannerImageResult.NotFound();
        }

        var contentType = await ImageContentTypeSniffer.DetectAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        return GetBannerImageResult.Success(imageRef.Sha256, contentType, stream);
    }

    /// <summary>
    /// Compara If-None-Match contra el ETag actual. Soporta multiples valores separados por
    /// coma y el comodin * (RFC 7232 S3.2), aunque en la practica el navegador manda un unico
    /// valor con comillas.
    /// </summary>
    private static bool MatchesEtag(string ifNoneMatch, string currentEtag) =>
        ifNoneMatch.Split(',').Select(v => v.Trim()).Any(v => v == "*" || v == currentEtag);
}
