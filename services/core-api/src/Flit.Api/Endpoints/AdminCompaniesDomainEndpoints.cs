using System.Security.Claims;
using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.GetDomain;
using Flit.Admin.Application.Companies.Domains.RegisterDomain;
using Flit.Admin.Application.Companies.Domains.RemoveDomain;
using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Consola del SuperAdmin sobre el dominio dedicado de una cabeza MARCA_BLANCA (HU #12416,
/// Feature #12368, ADR-0060 D1). Contrato: <c>.claude/state/marca-blanca/diseno/contratos-api.md</c> §3.
/// Exclusivo SuperAdmin (sin <c>CompanyOwnTenantFilter</c>, patrón <see cref="AdminCompaniesBrandingEndpoints"/>);
/// la autogestión de solo lectura de la cabeza vive en <see cref="CompanyDomainEndpoints"/>.
/// </summary>
public static class AdminCompaniesDomainEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompaniesDomainEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/domain")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Dominio de la red");

        group.MapGet("", GetDomainAsync)
            .WithName("AdminDomainGet")
            .WithSummary("Obtiene el dominio registrado de una cabeza MARCA_BLANCA")
            .Produces<TenantDomainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("", RegisterDomainAsync)
            .WithName("AdminDomainRegister")
            .WithSummary("Registra o cambia el dominio de una cabeza MARCA_BLANCA")
            .Produces<TenantDomainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("", RemoveDomainAsync)
            .WithName("AdminDomainRemove")
            .WithSummary("Retira el dominio vigente (el dato se conserva)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/verify", VerifyDomainAsync)
            .WithName("AdminDomainVerify")
            .WithSummary("Comprueba a demanda el registro TXT de titularidad (HU #12425 AC2)")
            .Produces<TenantDomainResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static async Task<IResult> VerifyDomainAsync(
        Guid tenantId,
        HttpContext httpContext,
        [FromServices] VerifyDomainHandler handler,
        [FromServices] DomainOptions options,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tenantId, ResolveUserId(httpContext.User), cancellationToken).ConfigureAwait(false);
        return ToVerifyResult(result, tenantId, options.EdgeTarget);
    }

    internal static IResult ToVerifyResult(VerifyDomainResult result, Guid tenantId, string edgeTarget) => result.Outcome switch
    {
        VerifyDomainOutcome.NotFound => NotFoundResponse(tenantId),
        VerifyDomainOutcome.Cooldown => Results.Json(
            new { error = DomainErrors.VerificationCooldown, message = "La comprobación se pidió hace muy poco. Intenta de nuevo más tarde.", retryAfterSeconds = result.RetryAfterSeconds },
            statusCode: StatusCodes.Status429TooManyRequests),
        _ => Results.Ok(TenantDomainResponse.From(result.Domain!, edgeTarget)),
    };

    internal static async Task<IResult> GetDomainAsync(
        Guid tenantId,
        [FromServices] GetDomainHandler handler,
        [FromServices] DomainOptions options,
        CancellationToken cancellationToken)
    {
        var domain = await handler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return domain is null
            ? NotFoundResponse(tenantId)
            : Results.Ok(TenantDomainResponse.From(domain, options.EdgeTarget));
    }

    private static async Task<IResult> RegisterDomainAsync(
        Guid tenantId,
        RegisterDomainRequestBody? body,
        HttpContext httpContext,
        [FromServices] RegisterDomainHandler handler,
        [FromServices] DomainOptions options,
        CancellationToken cancellationToken)
    {
        var command = new RegisterDomainCommand
        {
            TenantId = tenantId,
            Host = body?.Host,
            RowVersion = body?.RowVersion,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return ToResult(result, tenantId, options.EdgeTarget);
    }

    private static async Task<IResult> RemoveDomainAsync(
        Guid tenantId,
        HttpContext httpContext,
        [FromServices] RemoveDomainHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new RemoveDomainCommand { TenantId = tenantId, ChangedBy = ResolveUserId(httpContext.User) };
        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.Outcome == RemoveDomainOutcome.Retired
            ? Results.NoContent()
            : NotFoundResponse(tenantId);
    }

    internal static IResult ToResult(RegisterDomainResult result, Guid tenantId, string edgeTarget) => result.Outcome switch
    {
        RegisterDomainOutcome.Registered => Results.Ok(TenantDomainResponse.From(result.Domain!, edgeTarget)),
        RegisterDomainOutcome.Conflict => Results.Json(
            new { error = DomainErrors.ConcurrencyConflict, message = "El dominio fue modificado por otra persona. Recarga e intenta de nuevo." },
            statusCode: StatusCodes.Status409Conflict),
        RegisterDomainOutcome.TenantNotMarcaBlanca => Results.Json(
            new { error = DomainErrors.TenantNotMarcaBlanca, message = $"El tenant {tenantId} no es una cabeza de tipo MARCA_BLANCA." },
            statusCode: StatusCodes.Status409Conflict),
        RegisterDomainOutcome.HostAlreadyRegistered => Results.Json(
            new { error = DomainErrors.HostAlreadyRegistered, message = "El dominio ya está registrado y vigente para otra red." },
            statusCode: StatusCodes.Status409Conflict),
        RegisterDomainOutcome.AlreadyRegisteredForTenant => Results.Json(
            new { error = DomainErrors.AlreadyRegisteredForTenant, message = $"El tenant {tenantId} ya tiene un dominio vigente." },
            statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { error = result.ErrorCode }, statusCode: StatusCodes.Status400BadRequest),
    };

    internal static IResult NotFoundResponse(Guid tenantId) =>
        Results.NotFound(new { error = DomainErrors.NotFound, message = $"El tenant {tenantId} no tiene un dominio registrado." });

    internal static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Cuerpo de <c>PUT .../domain</c> (HU #12416 AC1/AC5): <c>host</c> requerido, <c>rowVersion</c> opcional (ausente en el primer registro).</summary>
    public sealed record RegisterDomainRequestBody(string? Host, long? RowVersion);
}
