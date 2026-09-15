using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Microsoft.Extensions.Logging;

namespace Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;

/// <summary>
/// Resolución pública de marca por dominio, ANTES de sesión (HU #12418 AC1-AC3, AC6-AC9, ADR-0060
/// D2). Endpoint: <c>GET /api/v1/public/branding</c>.
///
/// <para><b>Entrada por valores, no por <c>DomainContext</c></b>: el sello lo resuelve
/// <c>Flit.Api.Middleware.DomainContextMiddleware</c> vía <c>IDomainContextAccessor</c>
/// (<c>Flit.Modules.Security.Application.Auth</c>), pero ESE proyecto ya referencia
/// <c>Flit.Admin.Application</c> (HU #10678) — referenciarlo de vuelta aquí crearía un ciclo. El
/// endpoint (que sí ve ambos) traduce <c>DomainContext</c> a <paramref name="isNetworkDomain"/>/
/// <paramref name="headTenantId"/> antes de invocar este handler.</para>
///
/// <para><b>Kind Flit (dominio de FLIT, sin sello o host reservado)</b>: responde
/// <see cref="BrandIdentity.Flit"/> INMEDIATAMENTE — sin tocar caché ni repositorio (AC8: paridad
/// total, cero acceso a datos de ninguna red).</para>
///
/// <para><b>Kind Network</b>: cachea POR <paramref name="headTenantId"/> (no por host — ver
/// <see cref="IPublicBrandingCache"/>) durante 60 s, positivo y negativo por el MISMO camino
/// (AC2/AC7 anti-enumeración: mismo costo/forma para "cabeza sin publicar" que para "marca
/// publicada"). En caché fría: relee el ESTADO del tenant (<see cref="IBrandingTenantLookup"/>) —
/// SIGUE MARCA_BLANCA + cabeza de grupo + activa — y solo entonces el borrador publicado
/// (<see cref="ITenantBrandingRepository"/>). Cualquier fallo (BD, storage) o condición negativa ⇒
/// <see cref="BrandIdentity.Flit"/> + log <see cref="LogLevel.Warning"/> (AC6): nunca un error al
/// visitante, nunca se bloquea la pantalla de acceso.</para>
/// </summary>
public sealed class ResolvePublicBrandingHandler
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly ITenantBrandingRepository _brandingRepository;
    private readonly IBrandingTenantLookup _tenantLookup;
    private readonly IPublicBrandingCache _cache;
    private readonly ILogger<ResolvePublicBrandingHandler> _logger;

    public ResolvePublicBrandingHandler(
        ITenantBrandingRepository brandingRepository,
        IBrandingTenantLookup tenantLookup,
        IPublicBrandingCache cache,
        ILogger<ResolvePublicBrandingHandler> logger)
    {
        _brandingRepository = brandingRepository ?? throw new ArgumentNullException(nameof(brandingRepository));
        _tenantLookup = tenantLookup ?? throw new ArgumentNullException(nameof(tenantLookup));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BrandIdentityResponse> HandleAsync(
        bool isNetworkDomain,
        Guid? headTenantId,
        CancellationToken cancellationToken = default)
    {
        // AC8 — dominio de FLIT: paridad total, CERO acceso a caché o repositorio.
        if (!isNetworkDomain || headTenantId is not { } tenantId)
        {
            return BrandIdentityResponse.From(BrandIdentity.Flit);
        }

        if (_cache.TryGet(tenantId, out var cached))
        {
            return BrandIdentityResponse.From(cached);
        }

        var identity = await ResolveUncachedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        _cache.Store(tenantId, identity, CacheTtl);
        return BrandIdentityResponse.From(identity);
    }

    private async Task<BrandIdentity> ResolveUncachedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var tenant = await _tenantLookup.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);

            // AC2/AC7 — releído en caliente: si la cabeza dejó de ser MARCA_BLANCA, de ser cabeza de
            // grupo o se inactivó, el negativo comparte camino y caché con el positivo.
            if (tenant is null
                || !string.Equals(tenant.TenantType, HeadTenantTypes.MarcaBlanca, StringComparison.Ordinal)
                || !tenant.IsGroupParent
                || !tenant.IsActive)
            {
                return BrandIdentity.Flit;
            }

            var branding = await _brandingRepository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (branding is null || branding.IsRetired || branding.Published is not { } published)
            {
                return BrandIdentity.Flit;
            }

            return new BrandIdentity(
                published.PlatformName ?? BrandIdentity.Flit.PlatformName,
                published.LogoId,
                published.Colors ?? BrandIdentity.Flit.Colors,
                branding.PublishedVersion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PublicBrandingResolutionLog.ResolveFailed(_logger, tenantId, ex);
            return BrandIdentity.Flit;
        }
    }
}

/// <summary>Logging source-generado (CA1848) de <see cref="ResolvePublicBrandingHandler"/>.</summary>
internal static partial class PublicBrandingResolutionLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al resolver la marca pública del tenant {TenantId}; se responde la identidad de FLIT (HU #12418 AC6).")]
    public static partial void ResolveFailed(ILogger logger, Guid tenantId, Exception ex);
}
