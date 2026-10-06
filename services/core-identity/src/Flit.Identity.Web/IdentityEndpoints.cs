using Flit.Api.Endpoints;
using Flit.Api.Endpoints.Platform;
using Flit.Api.Endpoints.Public;
using Flit.Api.Identity;

namespace Flit.Identity.Web;

/// <summary>
/// Las rutas del login (Epic #13217, frontera §4): las que necesitan el hub y <c>@flit/auth</c> para que el login
/// sobreviva a una caída de core-api. Las mapea core-identity y, durante la transición, también core-api.
/// </summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapFlitIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints(); // /api/v1/auth/*
        app.MapPlatformEndpoints(); // /api/v1/platform/* · HU #12966
        app.MapPublicBrandingEndpoints(); // /api/v1/public/branding* · HU #12418
        app.MapFlitOidcEndpoints(); // /connect/*, /api/v1/platform/issuers · HU #12990 (/connect/* con Suite:Oidc:Enabled)
        return app;
    }
}
