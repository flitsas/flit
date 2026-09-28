using System.Security.Claims;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Modules.Platform.Application.Hosts;
using Flit.Modules.Security.Application.Auth.Login;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Api.Identity;

/// <summary>
/// Endpoints del servidor OIDC del hub (FLIT Suite A-05, HU #12990). <c>/connect/*</c> solo existe con
/// <c>Suite:Oidc:Enabled</c>; sin la bandera responden 404 y el login actual (<c>/api/v1/auth/login</c>) no cambia.
/// El descubrimiento y el JWKS los atiende OpenIddict directamente.
/// </summary>
public static class OidcEndpoints
{
    public static IEndpointRouteBuilder MapFlitOidcEndpoints(this IEndpointRouteBuilder app)
    {
        // Contrato §6: emisores que los servicios aceptan (caché de 5 minutos del lado del consumidor).
        app.MapGet("/api/v1/platform/issuers", async (IProductHosts hosts, ITenantDomainRepository domains, IConfiguration configuration, CancellationToken ct) =>
            Results.Ok(await OidcIssuerRegistry.ListAsync(hosts, domains, configuration["Suite:Hosts:Scheme"] ?? "https", ct).ConfigureAwait(false))).AllowAnonymous().WithTags("Platform").WithName("ListIssuers");

        if (!app.ServiceProvider.GetRequiredService<IOptions<OidcOptions>>().Value.Enabled)
            return app;

        app.MapPost("/connect/login", LoginAsync).AllowAnonymous().DisableAntiforgery().WithTags("Identity");
        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], (Delegate)AuthorizeAsync).AllowAnonymous().DisableAntiforgery().WithTags("Identity");
        app.MapPost("/connect/token", TokenAsync).AllowAnonymous().DisableAntiforgery().WithTags("Identity");
        app.MapMethods("/connect/logout", [HttpMethods.Get, HttpMethods.Post], (Delegate)LogoutAsync).AllowAnonymous().DisableAntiforgery().WithTags("Identity");
        return app;
    }

    /// <summary>
    /// Login del hub (lo usa la pantalla de A-06): verifica con <see cref="CredentialVerifier"/> —las mismas reglas y
    /// la misma auditoría que el login actual— y abre la sesión del hub. No emite tokens: eso lo hace authorize.
    /// </summary>
    private static async Task<IResult> LoginAsync(HttpContext http, [FromBody] HubLoginRequest request, CredentialVerifier verifier, CancellationToken ct)
    {
        VerifiedCredentials verified;
        try
        {
            verified = await verifier.VerifyAsync(request.Email, request.Password, ct).ConfigureAwait(false);
        }
        catch (InvalidCredentialsException)
        {
            return Error("INVALID_CREDENTIALS", "Invalid credentials.", StatusCodes.Status401Unauthorized);
        }
        catch (NetworkDomainRequiredException ex)
        {
            return Results.Json(
                new HubNetworkDomainRequired("NETWORK_DOMAIN_REQUIRED", ex.NetworkDomain, $"https://{ex.NetworkDomain}/login"),
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (AccountSuspendedException)
        {
            return Error("ACCOUNT_TEMPORARILY_BLOCKED", "La cuenta está bloqueada temporalmente.", StatusCodes.Status403Forbidden);
        }
        catch (AllRolesInactiveException)
        {
            return Error("ALL_ROLES_INACTIVE", "Todos los roles asignados al usuario están inactivos.", StatusCodes.Status403Forbidden);
        }

        var identity = new ClaimsIdentity(OidcDefaults.HubSessionScheme);
        identity.AddClaim(new Claim(Claims.Subject, verified.User.UserId.ToString()));
        identity.AddClaim(new Claim(Claims.Email, verified.User.Email));
        identity.AddClaim(new Claim(OidcDefaults.TenantIdClaim, verified.User.TenantId.ToString()));
        identity.AddClaim(new Claim(OidcDefaults.DomainClaim, verified.DomainClaim));
        if (verified.IsSuperAdmin)
            identity.AddClaim(new Claim(OidcDefaults.SuperAdminClaim, "true"));

        await http.SignInAsync(OidcDefaults.HubSessionScheme, new ClaimsPrincipal(identity)).ConfigureAwait(false);
        return Results.Ok(new HubLoginResponse(SafeReturnUrl(request.ReturnUrl)));
    }

    private static async Task<IResult> AuthorizeAsync(
        HttpContext http, OidcPrincipalFactory factory, IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations, CancellationToken ct)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("La petición OIDC no llegó al servidor.");

        var session = await http.AuthenticateAsync(OidcDefaults.HubSessionScheme).ConfigureAwait(false);
        if (!session.Succeeded || request.HasPromptValue(PromptValues.Login))
        {
            if (request.HasPromptValue(PromptValues.None))
                return Forbid(Errors.LoginRequired, "Se requiere iniciar sesión.");

            // Sin sesión: al login del hub y de vuelta a esta misma petición (ver OnRedirectToLogin).
            return Results.Challenge(new AuthenticationProperties(), [OidcDefaults.HubSessionScheme]);
        }

        var grant = await factory.ForUserAsync(session.Principal!, request.ClientId!, request.GetScopes(), ct).ConfigureAwait(false);
        if (grant.Principal is null)
        {
            // HU #12992 (A-07): la cuenta ya no sirve para abrir sesión, se cierra la del hub y se vuelve a pedir.
            if (grant.DeniedCode == OidcPrincipalFactory.SessionInvalid)
            {
                await http.SignOutAsync(OidcDefaults.HubSessionScheme).ConfigureAwait(false);
                return request.HasPromptValue(PromptValues.None)
                    ? Forbid(Errors.LoginRequired, grant.DeniedCode)
                    : Results.Challenge(new AuthenticationProperties(), [OidcDefaults.HubSessionScheme]);
            }

            // Producto apagado o sin rol (contrato §2 y §10): no hay código, el producto recibe access_denied.
            return Forbid(Errors.AccessDenied, grant.DeniedCode!);
        }

        // HU #13004 (A-13): una autorización explícita por (sesión del hub, producto). Sus refresh tokens cuelgan de
        // ella y su id queda en la sesión del hub: al cerrar sesión se revocan solo las de ESTE navegador.
        var application = await applications.FindByClientIdAsync(request.ClientId!, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cliente OIDC desconocido.");
        var authorization = await authorizations.CreateAsync(
            grant.Principal,
            grant.Principal.GetClaim(Claims.Subject)!,
            (await applications.GetIdAsync(application, ct).ConfigureAwait(false))!,
            AuthorizationTypes.AdHoc,
            grant.Principal.GetScopes(),
            ct).ConfigureAwait(false);
        var authorizationId = (await authorizations.GetIdAsync(authorization, ct).ConfigureAwait(false))!;
        grant.Principal.SetAuthorizationId(authorizationId);
        await RememberAuthorizationAsync(http, session, authorizationId).ConfigureAwait(false);

        return Results.SignIn(grant.Principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>Agrega la autorización a la sesión del hub (se guardan las últimas <see cref="MaxSessionAuthorizations"/>).</summary>
    private static async Task RememberAuthorizationAsync(HttpContext http, AuthenticateResult session, string authorizationId)
    {
        var identity = new ClaimsIdentity(session.Principal!.Claims.Where(c => c.Type != OidcDefaults.AuthorizationClaim), OidcDefaults.HubSessionScheme);
        foreach (var id in session.Principal!.FindAll(OidcDefaults.AuthorizationClaim).Select(c => c.Value).Append(authorizationId).TakeLast(MaxSessionAuthorizations))
            identity.AddClaim(new Claim(OidcDefaults.AuthorizationClaim, id));
        await http.SignInAsync(OidcDefaults.HubSessionScheme, new ClaimsPrincipal(identity), session.Properties).ConfigureAwait(false);
    }

    private const int MaxSessionAuthorizations = 20;

    private static async Task<IResult> TokenAsync(HttpContext http, OidcPrincipalFactory factory, CancellationToken ct)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("La petición OIDC no llegó al servidor.");

        if (request.IsClientCredentialsGrantType())
            return Results.SignIn(factory.ForService(request.ClientId!, request.GetScopes()), null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        // authorization_code y refresh_token: OpenIddict ya validó el código o el refresh (y lo rotó).
        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme).ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is null)
            return Forbid(Errors.InvalidGrant, "El código o el refresh token ya no es válido.");

        // HU #12992 (A-07): en cada canje —también en cada refresh— se relee el usuario y su acceso al producto.
        var grant = await factory.ForUserAsync(result.Principal, request.ClientId!, result.Principal.GetScopes(), ct).ConfigureAwait(false);
        if (grant.Principal is null)
            return Forbid(Errors.InvalidGrant, grant.DeniedCode!);

        // Los refresh nuevos siguen colgando de la misma autorización (A-13): revocarla corta toda la cadena.
        if (result.Principal.GetAuthorizationId() is { } authorizationId)
            grant.Principal.SetAuthorizationId(authorizationId);
        return Results.SignIn(grant.Principal, null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Cierra la sesión del hub y vuelve al <c>post_logout_redirect_uri</c> registrado del producto. HU #13004 (A-13):
    /// revoca las autorizaciones de esta sesión y sus tokens, así los demás productos abiertos en este navegador pierden
    /// la sesión en su siguiente renovación (≤ 15 min). Las sesiones de otros dispositivos no se tocan.
    /// </summary>
    private static async Task<IResult> LogoutAsync(HttpContext http, IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens, CancellationToken ct)
    {
        var session = await http.AuthenticateAsync(OidcDefaults.HubSessionScheme).ConfigureAwait(false);
        foreach (var id in session.Principal?.FindAll(OidcDefaults.AuthorizationClaim).Select(c => c.Value) ?? [])
        {
            await tokens.RevokeByAuthorizationIdAsync(id, ct).ConfigureAwait(false);
            if (await authorizations.FindByIdAsync(id, ct).ConfigureAwait(false) is { } authorization)
                await authorizations.TryRevokeAsync(authorization, ct).ConfigureAwait(false);
        }

        await http.SignOutAsync(OidcDefaults.HubSessionScheme).ConfigureAwait(false);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    private static IResult Forbid(string error, string description) =>
        Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static IResult Error(string code, string message, int status) =>
        Results.Json(new HubError(code, message), statusCode: status);

    /// <summary>Solo rutas relativas del mismo host: evita un redireccionamiento abierto tras el login.</summary>
    internal static string? SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : null;

    private sealed record HubLoginRequest(string Email, string Password, string? ReturnUrl);

    private sealed record HubLoginResponse(string? ReturnUrl);

    private sealed record HubError(string Code, string Message);

    private sealed record HubNetworkDomainRequired(string Error, string NetworkDomain, string LoginUrl);
}
