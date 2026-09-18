using Flit.Admin.Application.Banners.GetBannerImage;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Microsoft.Extensions.Logging;

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
public sealed partial class GetPublicBrandLogoHandler
{
    private readonly ITenantBrandingRepository _brandingRepository;
    private readonly IBrandingTenantLookup _tenantLookup;
    private readonly IBrandLogoStorage _storage;
    private readonly ILogger<GetPublicBrandLogoHandler> _logger;

    public GetPublicBrandLogoHandler(
        ITenantBrandingRepository brandingRepository,
        IBrandingTenantLookup tenantLookup,
        IBrandLogoStorage storage,
        ILogger<GetPublicBrandLogoHandler> logger)
    {
        _brandingRepository = brandingRepository ?? throw new ArgumentNullException(nameof(brandingRepository));
        _tenantLookup = tenantLookup ?? throw new ArgumentNullException(nameof(tenantLookup));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        // HU #12429 AC5 — un fallo del storage (excepción de transporte, no solo "archivo
        // ausente") NUNCA debe convertirse en un 500 del endpoint público: mismo respaldo
        // "controlado" que ResolvePublicBrandingHandler aplica a un fallo de BD.
        Stream? stream;
        try
        {
            stream = await _storage.OpenReadAsync(logo.StoragePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStorageOpenFailed(_logger, logo.TenantId, ex);
            return GetPublicBrandLogoResult.NotFound();
        }

        if (stream is null)
        {
            return GetPublicBrandLogoResult.NotFound();
        }

        var contentType = await ImageContentTypeSniffer.DetectAsync(stream, cancellationToken).ConfigureAwait(false);
        return GetPublicBrandLogoResult.Success(logo.StorageSha256, contentType, stream);
    }

    private static bool MatchesEtag(string ifNoneMatch, string currentEtag) =>
        ifNoneMatch.Split(',').Select(v => v.Trim()).Any(v => v == "*" || v == currentEtag);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al abrir el logotipo del tenant {TenantId} en el storage; se responde 404 controlado (HU #12429 AC5).")]
    private static partial void LogStorageOpenFailed(ILogger logger, Guid tenantId, Exception ex);
}
