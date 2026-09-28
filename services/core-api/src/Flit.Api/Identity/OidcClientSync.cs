using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Api.Identity;

/// <summary>
/// Registra los clientes OIDC al arrancar (FLIT Suite A-05, HU #12990), creándolos o actualizándolos para que la base
/// siempre refleje la configuración:
/// <list type="bullet">
/// <item><b>Productos</b> (<see cref="ProductCodes.All"/>): <c>client_id</c> = código del producto, así <c>aud</c> sale sin
/// mapeos (espiga A-04). Públicos, authorization code con PKCE obligatorio y refresh; retorno a
/// <c>&lt;URL del producto&gt;/auth/callback</c> en este ambiente (<c>Suite:Hosts</c>) más los adicionales configurados.</item>
/// <item><b>Servicios</b> (<c>Suite:Oidc:ServiceClients</c>): confidenciales, client credentials con sus scopes (contrato §3).
/// Sin secreto no se registran.</item>
/// </list>
/// </summary>
internal sealed partial class OidcClientSync(IServiceProvider services, ILogger<OidcClientSync> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var hosts = services.GetRequiredService<IProductHosts>();
        var options = services.GetRequiredService<IOptions<OidcOptions>>().Value;

        foreach (var product in ProductCodes.All)
            await UpsertAsync(ProductClient(product, hosts, options), cancellationToken).ConfigureAwait(false);

        foreach (var (clientId, service) in options.ServiceClients)
        {
            if (string.IsNullOrWhiteSpace(service.Secret))
            {
                LogServiceClientSkipped(logger, clientId);
                continue;
            }

            await UpsertAsync(ServiceClient(clientId, service), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static OpenIddictApplicationDescriptor ProductClient(string product, IProductHosts hosts, OidcOptions options)
    {
        var baseUrl = hosts.UrlFor(product).TrimEnd('/');
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = product,
            DisplayName = product,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        descriptor.RedirectUris.Add(new Uri(baseUrl + options.CallbackPath));
        descriptor.PostLogoutRedirectUris.Add(new Uri(baseUrl + "/"));
        if (options.ExtraRedirectUris.TryGetValue(product, out var extra))
        {
            foreach (var uri in extra)
                descriptor.RedirectUris.Add(new Uri(uri));
        }

        return descriptor;
    }

    internal static OpenIddictApplicationDescriptor ServiceClient(string clientId, OidcServiceClientOptions service)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = service.Secret,
            DisplayName = clientId,
            ClientType = ClientTypes.Confidential,
            Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials },
        };

        foreach (var scope in service.Scopes)
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);

        return descriptor;
    }

    /// <summary>
    /// Crear o actualizar. Varias instancias arrancan a la vez (réplicas, despliegue): si otra ya escribió el mismo
    /// cliente, se relee y se reintenta; al tercer choque se deja, porque la otra instancia escribió lo mismo.
    /// </summary>
    private async Task UpsertAsync(OpenIddictApplicationDescriptor descriptor, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Un scope (y un DbContext) por intento: con el mismo, EF devolvería la entidad vieja que ya tiene rastreada.
            await using var scope = services.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            try
            {
                var existing = await manager.FindByClientIdAsync(descriptor.ClientId!, ct).ConfigureAwait(false);
                if (existing is null)
                    await manager.CreateAsync(descriptor, ct).ConfigureAwait(false);
                else
                    await manager.UpdateAsync(existing, descriptor, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is OpenIddictExceptions.ConcurrencyException or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                if (attempt >= 3)
                {
                    LogConcurrentSync(logger, descriptor.ClientId!);
                    return;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Cliente OIDC {ClientId} sincronizado por otra instancia al mismo tiempo; se deja su versión.")]
    private static partial void LogConcurrentSync(ILogger logger, string clientId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cliente OIDC de servicio {ClientId} sin secreto: no se registra.")]
    private static partial void LogServiceClientSkipped(ILogger logger, string clientId);
}
