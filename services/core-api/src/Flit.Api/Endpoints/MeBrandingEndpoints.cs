using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.ResolveSessionBranding;
using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// Herencia de marca YA autenticado (HU #12418 AC5/AC6, Feature #12366, ADR-0060 D2). Contrato:
/// <c>.claude/state/marca-blanca/diseno/contratos-api.md</c> §2. Cualquier usuario autenticado
/// (<c>RequireAuthorization()</c> sin policy adicional) — el tenant SALE del
/// JWT vía <see cref="RequestTenantResolver"/>, nunca de un parámetro: nadie puede pedir la marca de
/// otra red (AC5).
/// </summary>
public static class MeBrandingEndpoints
{
    public static IEndpointRouteBuilder MapMeBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/me/branding", GetSessionBrandingAsync)
            .WithName("GetSessionBranding")
            .WithTags("Sesión · Identidad de marca")
            .RequireAuthorization()
            .WithSummary("Obtiene la identidad de marca heredada por el usuario autenticado")
            .Produces<BrandIdentityResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetSessionBrandingAsync(
        HttpContext httpContext,
        ResolveSessionBrandingHandler handler,
        CancellationToken cancellationToken)
    {
        var isSuperAdmin = RequestTenantResolver.IsSuperAdmin(httpContext.User);
        var tenantId = RequestTenantResolver.ResolveTenantIdOrNull(httpContext.User) ?? Guid.Empty;

        var result = await handler.HandleAsync(tenantId, isSuperAdmin, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
