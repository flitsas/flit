using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Microsoft.Extensions.Logging;

namespace Flit.Admin.Application.Companies.Branding.ResolveSessionBranding;

/// <summary>
/// Herencia de marca dentro de la aplicación, YA autenticado (HU #12418 AC5/AC6, ADR-0060 D2).
/// Endpoint: <c>GET /api/v1/me/branding</c>. Sin parámetros de tenant: NADIE puede pedir la marca de
/// otra red (AC5, última cláusula) — <paramref name="sessionTenantId"/> sale SIEMPRE de
/// <c>RequestTenantResolver</c> en el endpoint (JWT), nunca de un header/query del cliente.
///
/// <list type="bullet">
///   <item>SuperAdmin (<paramref name="isSuperAdmin"/>) ⇒ FLIT: no tiene "tenant propio" de red.</item>
///   <item>Cabeza MARCA_BLANCA activa ⇒ su propia marca publicada.</item>
///   <item>Hija de una cabeza MARCA_BLANCA activa (<c>parent_tenant_id</c>, NUNCA el token) ⇒ la
///   marca publicada de la cabeza.</item>
///   <item>Concesión, hija de Concesión, compañía sin red, o cabeza MARCA_BLANCA sin publicar ⇒
///   FLIT.</item>
///   <item>Cualquier fallo (BD) ⇒ FLIT + log <see cref="LogLevel.Warning"/> (AC6): nunca bloquea la
///   sesión ya iniciada.</item>
/// </list>
/// </summary>
public sealed class ResolveSessionBrandingHandler
{
    private readonly IBrandingTenantLookup _tenantLookup;
    private readonly ITenantBrandingRepository _brandingRepository;
    private readonly ILogger<ResolveSessionBrandingHandler> _logger;

    public ResolveSessionBrandingHandler(
        IBrandingTenantLookup tenantLookup,
        ITenantBrandingRepository brandingRepository,
        ILogger<ResolveSessionBrandingHandler> logger)
    {
        _tenantLookup = tenantLookup ?? throw new ArgumentNullException(nameof(tenantLookup));
        _brandingRepository = brandingRepository ?? throw new ArgumentNullException(nameof(brandingRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BrandIdentityResponse> HandleAsync(
        Guid sessionTenantId,
        bool isSuperAdmin,
        CancellationToken cancellationToken = default)
    {
        if (isSuperAdmin || sessionTenantId == Guid.Empty)
        {
            return BrandIdentityResponse.From(BrandIdentity.Flit);
        }

        try
        {
            var headTenantId = await ResolveHeadTenantIdAsync(sessionTenantId, cancellationToken).ConfigureAwait(false);
            if (headTenantId is not { } tenantId)
            {
                return BrandIdentityResponse.From(BrandIdentity.Flit);
            }

            var branding = await _brandingRepository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (branding is null || branding.IsRetired || branding.Published is not { } published)
            {
                return BrandIdentityResponse.From(BrandIdentity.Flit);
            }

            var identity = new BrandIdentity(
                published.PlatformName ?? BrandIdentity.Flit.PlatformName,
                published.LogoId,
                published.Colors ?? BrandIdentity.Flit.Colors,
                branding.PublishedVersion);
            return BrandIdentityResponse.From(identity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SessionBrandingResolutionLog.ResolveFailed(_logger, sessionTenantId, ex);
            return BrandIdentityResponse.From(BrandIdentity.Flit);
        }
    }

    /// <summary><c>null</c> = ninguna cabeza MARCA_BLANCA activa aplica (Concesión, sin red, hija de
    /// Concesión, cabeza inexistente/inactiva).</summary>
    private async Task<Guid?> ResolveHeadTenantIdAsync(Guid sessionTenantId, CancellationToken cancellationToken)
    {
        var own = await _tenantLookup.GetAsync(sessionTenantId, cancellationToken).ConfigureAwait(false);
        if (own is null)
        {
            return null;
        }

        if (IsActiveMarcaBlancaHead(own))
        {
            return own.TenantId;
        }

        if (own.ParentTenantId is not { } parentTenantId)
        {
            return null;
        }

        var parent = await _tenantLookup.GetAsync(parentTenantId, cancellationToken).ConfigureAwait(false);
        return parent is not null && IsActiveMarcaBlancaHead(parent) ? parent.TenantId : null;
    }

    private static bool IsActiveMarcaBlancaHead(BrandingTenantSnapshot tenant) =>
        tenant.IsGroupParent
        && tenant.IsActive
        && string.Equals(tenant.TenantType, HeadTenantTypes.MarcaBlanca, StringComparison.Ordinal);
}

/// <summary>Logging source-generado (CA1848) de <see cref="ResolveSessionBrandingHandler"/>.</summary>
internal static partial class SessionBrandingResolutionLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al resolver la marca de sesión del tenant {TenantId}; se responde la identidad de FLIT (HU #12418 AC6).")]
    public static partial void ResolveFailed(ILogger logger, Guid tenantId, Exception ex);
}
