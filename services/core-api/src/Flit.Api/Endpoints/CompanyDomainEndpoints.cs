using System.Security.Claims;
using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.GetDomain;
using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Autogestión de solo lectura del dominio por la propia cabeza de grupo (HU #12416, #12427 AC1/AC2,
/// Feature #12368). Contrato: <c>.claude/state/marca-blanca/diseno/contratos-api.md</c> §4. Registrar,
/// cambiar o retirar el dominio es exclusivo del SuperAdmin (<see cref="AdminCompaniesDomainEndpoints"/>);
/// aquí no hay <c>PUT</c>/<c>DELETE</c>. Sin <c>{tenantId}</c> en la ruta: el tenant sale del JWT
/// (<see cref="RequestTenantResolver"/>), nunca de un header/param crudo — mismo patrón que
/// <see cref="CompanyBrandingEndpoints"/>. La policy <see cref="AdminAuthorization.MarcaBlancaHeadCompanyPolicy"/>
/// (HU #12429, endurecimiento del hecho 88) exige además clase MARCA_BLANCA: una Concesión con hijas
/// no puede autogestionar dominio de red.
/// </summary>
public static class CompanyDomainEndpoints
{
    public static IEndpointRouteBuilder MapCompanyDomainEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/company/domain")
            .RequireAuthorization(AdminAuthorization.MarcaBlancaHeadCompanyPolicy)
            .WithTags("Compañía · Dominio de la red");

        group.MapGet("", GetDomainAsync)
            .WithName("CompanyDomainGet")
            .WithSummary("Obtiene el dominio registrado de la propia cabeza de grupo")
            .Produces<TenantDomainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/verify", VerifyDomainAsync)
            .WithName("CompanyDomainVerify")
            .WithSummary("Comprueba a demanda el registro TXT de titularidad de la propia red (HU #12427 AC2)")
            .Produces<TenantDomainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> VerifyDomainAsync(
        HttpContext httpContext,
        [FromServices] VerifyDomainHandler handler,
        [FromServices] DomainOptions options,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(tenant.Value, ResolveUserId(httpContext.User), cancellationToken).ConfigureAwait(false);
        return AdminCompaniesDomainEndpoints.ToVerifyResult(result, tenant.Value, options.EdgeTarget);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user) => AdminCompaniesDomainEndpoints.ResolveUserId(user);

    private static async Task<IResult> GetDomainAsync(
        HttpContext httpContext,
        [FromServices] GetDomainHandler handler,
        [FromServices] DomainOptions options,
        CancellationToken cancellationToken)
    {
        var tenant = ResolveOwnTenant(httpContext.User);
        if (tenant is null)
        {
            return Results.Unauthorized();
        }

        var domain = await handler.HandleAsync(tenant.Value, cancellationToken).ConfigureAwait(false);
        return domain is null
            ? AdminCompaniesDomainEndpoints.NotFoundResponse(tenant.Value)
            : Results.Ok(TenantDomainResponse.From(domain, options.EdgeTarget));
    }

    /// <summary>
    /// Tenant propio del caller (mismo patrón que <c>CompanyBrandingEndpoints.ResolveOwnTenant</c>):
    /// SuperAdmin no tiene "tenant propio" para este grupo (opera por
    /// <c>/admin/companies/{tenantId}/domain</c>); si no resuelve un <c>tenant_id</c> no vacío del JWT
    /// se rechaza — nunca se interpreta como "todos".
    /// </summary>
    private static Guid? ResolveOwnTenant(ClaimsPrincipal user) =>
        RequestTenantResolver.TryResolveNonEmptyTenantId(user, out var tenantId) ? tenantId : null;
}
