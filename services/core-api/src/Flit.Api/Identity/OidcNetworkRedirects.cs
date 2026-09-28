using Flit.Admin.Application.Companies.Domains;
using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Flit.Api.Identity;

/// <summary>
/// HU #12993 (FLIT Suite A-08) — Marca Blanca sobre OIDC. Los clientes registran los retornos de los hosts FLIT; los
/// dominios de las redes cambian en caliente (<c>admin.tenant_domains</c>), así que se aceptan además, sin registrarlos:
/// <list type="bullet">
/// <item>solo por el hub de una red (emisor = dominio de la red): desde el hub de FLIT no se vuelve a una red;</item>
/// <item>solo a un dominio <b>activo</b> de <b>esa misma</b> red, cuyo <c>purpose</c> sea el producto que pide
/// (<c>HUB</c> para plataforma, B-08);</item>
/// <item>solo por https y a la ruta de retorno fija (<c>/auth/callback</c>, o <c>/</c> tras cerrar sesión), sin query.</item>
/// </list>
/// Reemplaza los manejadores de OpenIddict que validan <c>redirect_uri</c> y <c>post_logout_redirect_uri</c>, y
/// conserva su regla para todo lo registrado.
/// </summary>
internal static class OidcNetworkRedirects
{
    public static void Register(OpenIddictServerBuilder server)
    {
        server.RemoveEventHandler(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor);
        server.AddEventHandler(OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseScopedHandler<ValidateRedirectUri>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build());

        server.RemoveEventHandler(OpenIddictServerHandlers.Session.ValidateClientPostLogoutRedirectUri.Descriptor);
        server.AddEventHandler(OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateEndSessionRequestContext>()
            .UseScopedHandler<ValidatePostLogoutRedirectUri>()
            .SetOrder(OpenIddictServerHandlers.Session.ValidateClientPostLogoutRedirectUri.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build());
    }

    /// <summary>
    /// <c>true</c> si <paramref name="uri"/> es un dominio activo de la misma red que atiende la petición, con el propósito
    /// del producto y la ruta de retorno esperada.
    /// </summary>
    internal static async Task<bool> IsNetworkRedirectAsync(
        string? uri, string? product, string expectedPath, IDomainContextAccessor domain, ITenantDomainResolver resolver, CancellationToken ct)
    {
        if (domain.Kind != DomainKind.Network || domain.HeadTenantId is not { } head)
            return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps
            || !parsed.IsDefaultPort || parsed.AbsolutePath != expectedPath || parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
            return false;

        var target = await resolver.ResolveAsync(parsed.IdnHost.ToLowerInvariant(), ct).ConfigureAwait(false);
        return target.IsNetwork
            && target.HeadTenantId == head
            && (product is null || string.Equals(target.ProductCode, product, StringComparison.Ordinal));
    }

    internal sealed class ValidateRedirectUri(
        IOpenIddictApplicationManager applications, IDomainContextAccessor domain, ITenantDomainResolver resolver, IOptions<OidcOptions> options)
        : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
    {
        public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
        {
            if (string.IsNullOrEmpty(context.RedirectUri))
                return;

            var application = await applications.FindByClientIdAsync(context.ClientId!, context.CancellationToken).ConfigureAwait(false);
            if (application is not null
                && await applications.ValidateRedirectUriAsync(application, context.RedirectUri, context.CancellationToken).ConfigureAwait(false))
            {
                return;
            }

            if (await IsNetworkRedirectAsync(context.RedirectUri, context.ClientId, options.Value.CallbackPath, domain, resolver, context.CancellationToken).ConfigureAwait(false))
                return;

            context.Reject(error: Errors.InvalidRequest, description: "El redirect_uri no es válido para este cliente.");
        }
    }

    internal sealed class ValidatePostLogoutRedirectUri(
        IOpenIddictApplicationManager applications, IDomainContextAccessor domain, ITenantDomainResolver resolver)
        : IOpenIddictServerHandler<ValidateEndSessionRequestContext>
    {
        public async ValueTask HandleAsync(ValidateEndSessionRequestContext context)
        {
            if (string.IsNullOrEmpty(context.PostLogoutRedirectUri))
                return;

            if (!string.IsNullOrEmpty(context.ClientId))
            {
                var application = await applications.FindByClientIdAsync(context.ClientId, context.CancellationToken).ConfigureAwait(false);
                if (application is not null
                    && await applications.ValidatePostLogoutRedirectUriAsync(application, context.PostLogoutRedirectUri, context.CancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            else
            {
                await foreach (var _ in applications.FindByPostLogoutRedirectUriAsync(context.PostLogoutRedirectUri, context.CancellationToken).ConfigureAwait(false))
                    return;
            }

            // Sin client_id se acepta cualquier producto de la misma red.
            var product = string.IsNullOrEmpty(context.ClientId) ? null : context.ClientId;
            if (await IsNetworkRedirectAsync(context.PostLogoutRedirectUri, product, "/", domain, resolver, context.CancellationToken).ConfigureAwait(false))
                return;

            context.Reject(error: Errors.InvalidRequest, description: "El post_logout_redirect_uri no es válido.");
        }
    }
}
