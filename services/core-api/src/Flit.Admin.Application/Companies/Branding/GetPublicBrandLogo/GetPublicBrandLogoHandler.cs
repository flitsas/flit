using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.Branding.GetPublicBrandLogo;

/// <summary>
/// Caso de uso de <c>GET /api/v1/public/branding/logos/{logoId}</c> (HU #12418 AC4). <c>logoId</c>
/// es un uuidv7 OPACO de <c>admin.tenant_brand_logos.id</c> — no un id de compañía — así que la
/// búsqueda es por id SOLO (no requiere tenant): la URL es la misma que devolvió
/// <c>BrandLogoResponse</c>/<c>BrandIdentityResponse</c> al publicar. Se sirve si la fila existe, no
/// está borrada y su tenant SIGUE MARCA_BLANCA (versiones <c>superseded</c> también — la URL es
/// inmutable, ADR-0060 D2). Publicar una versión nueva cambia <c>logoId</c> ⇒ cambia la URL, así que
/// la caché de la anterior nunca se reutiliza.
/// </summary>
public sealed class GetPublicBrandLogoHandler
{
    private readonly ITenantBrandingRepository _brandingRepository;
    private readonly IBrandingTenantLookup _tenantLookup;
    private readonly IBrandLogoStorage _storage;

    public GetPublicBrandLogoHandler(
        ITenantBrandingRepository brandingRepository,
        IBrandingTenantLookup tenantLookup,
        IBrandLogoStorage storage)
    {
        _brandingRepository = brandingRepository ?? throw new ArgumentNullException(nameof(brandingRepository));
        _tenantLookup = tenantLookup ?? throw new ArgumentNullException(nameof(tenantLookup));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public async Task<GetPublicBrandLogoResult> HandleAsync(
        Guid logoId,
        string? ifNoneMatch,
        CancellationToken cancellationToken = default)
    {
        var logo = await _brandingRepository.GetLogoVersionByIdAsync(logoId, cancellationToken).ConfigureAwait(false);
        if (logo is null)
        {
            return GetPublicBrandLogoResult.NotFound();
        }

        var tenant = await _tenantLookup.GetAsync(logo.TenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null || !string.Equals(tenant.TenantType, HeadTenantTypes.MarcaBlanca, StringComparison.Ordinal))
        {
            // Misma respuesta que "id inexistente" (AC4: 404 indistinguible).
            return GetPublicBrandLogoResult.NotFound();
        }

        var etag = $"\"{logo.StorageSha256}\"";
        if (!string.IsNullOrWhiteSpace(ifNoneMatch) && MatchesEtag(ifNoneMatch, etag))
        {
            return GetPublicBrandLogoResult.NotModified(logo.StorageSha256);
        }

        var stream = await _storage.OpenReadAsync(logo.StoragePath, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return GetPublicBrandLogoResult.NotFound();
        }

        var contentType = await ImageContentTypeSniffer.DetectAsync(stream, cancellationToken).ConfigureAwait(false);
        return GetPublicBrandLogoResult.Success(logo.StorageSha256, contentType, stream);
    }

    private static bool MatchesEtag(string ifNoneMatch, string currentEtag) =>
        ifNoneMatch.Split(',').Select(v => v.Trim()).Any(v => v == "*" || v == currentEtag);
}
