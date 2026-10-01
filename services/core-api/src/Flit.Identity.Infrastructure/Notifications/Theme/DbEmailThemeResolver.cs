using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Admin.Domain.Companies.Create;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.Notifications.Theme;

/// <summary>
/// Implementación de producción de <see cref="IEmailThemeResolver"/> (HU #12428 AC1/AC4/AC5).
/// Misma herencia que <c>ResolveSessionBrandingHandler</c> (#12418): cabeza MARCA_BLANCA activa con
/// marca publicada ⇒ su marca; hija de una cabeza así (<c>parent_tenant_id</c>, punto único #12320)
/// ⇒ la marca de la cabeza; cualquier otro caso ⇒ <see cref="EmailTheme.Flit"/>. Caché de 60 s por
/// <paramref name="tenantId"/> sobre el MISMO <see cref="IMemoryCache"/> singleton que
/// <c>MemoryPublicBrandingCache</c> — <c>InvalidateTenant</c> limpia AMBAS entradas (branding pública
/// y tema de correo) con una sola llamada.
/// </summary>
/// <remarks>
/// AC4 — cualquier excepción (BD, timeout) se atrapa AQUÍ, nunca se propaga: el envío del correo no
/// puede fallar por causa del tema. Se registra con <see cref="LogLevel.Warning"/> y se resuelve
/// <see cref="EmailTheme.Flit"/>.
/// </remarks>
internal sealed partial class DbEmailThemeResolver(
    IBrandingTenantLookup tenantLookup,
    ITenantBrandingRepository brandingRepository,
    IMemoryCache cache,
    EmailThemePublicBrandingOptions publicBrandingOptions,
    ILogger<DbEmailThemeResolver> logger) : IEmailThemeResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    internal const string CacheKeyPrefix = "flit:email-theme:tenant:";

    public async Task<EmailTheme> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId is not { } id || id == Guid.Empty)
        {
            return EmailTheme.Flit;
        }

        if (cache.TryGetValue<EmailTheme>(CacheKeyFor(id), out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var theme = await ResolveUncachedAsync(id, cancellationToken).ConfigureAwait(false);
            cache.Set(CacheKeyFor(id), theme, CacheTtl);
            return theme;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogResolveFailed(logger, id, ex);
            return EmailTheme.Flit;
        }
    }

    private async Task<EmailTheme> ResolveUncachedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var headTenantId = await ResolveHeadTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (headTenantId is not { } head)
        {
            return EmailTheme.Flit;
        }

        var branding = await brandingRepository.GetByTenantIdAsync(head, cancellationToken).ConfigureAwait(false);
        if (branding is null || branding.IsRetired || branding.Published is not { } published)
        {
            return EmailTheme.Flit;
        }

        return EmailThemeFactory.FromPublished(
            published, branding.PublishedVersion, publicBrandingOptions.PublicBaseUrl);
    }

    /// <summary><c>null</c> = ninguna cabeza MARCA_BLANCA activa aplica (Concesión, sin red, hija de
    /// Concesión, cabeza inexistente/inactiva) — mismo criterio que <c>ResolveSessionBrandingHandler</c>.</summary>
    private async Task<Guid?> ResolveHeadTenantIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var own = await tenantLookup.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
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

        var parent = await tenantLookup.GetAsync(parentTenantId, cancellationToken).ConfigureAwait(false);
        return parent is not null && IsActiveMarcaBlancaHead(parent) ? parent.TenantId : null;
    }

    private static bool IsActiveMarcaBlancaHead(BrandingTenantSnapshot tenant) =>
        tenant.IsGroupParent
        && tenant.IsActive
        && string.Equals(tenant.TenantType, HeadTenantTypes.MarcaBlanca, StringComparison.Ordinal);

    internal static string CacheKeyFor(Guid tenantId) => CacheKeyPrefix + tenantId;

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Fallo al resolver el tema de correo del tenant {TenantId}; se aplica el tema de FLIT (HU #12428 AC4).")]
    private static partial void LogResolveFailed(ILogger logger, Guid tenantId, Exception ex);
}
