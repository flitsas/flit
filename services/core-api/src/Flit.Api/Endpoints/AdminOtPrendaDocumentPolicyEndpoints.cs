using System.Security.Claims;
using Flit.Admin.Application.Companies.TransitOffices.OtPrendaDocumentPolicy;
using Flit.Admin.Domain.OtProfile;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Hub OT: listar/configurar opt-out de documento de prenda por compañía habilitada en el OT.
/// SuperAdmin u ot_admin (acotado a su OT).
/// </summary>
public static class AdminOtPrendaDocumentPolicyEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtPrendaDocumentPolicyEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/prenda-document-policies")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Prenda");

        group.MapGet("/", ListForOfficeAsync)
            .WithName("AdminOtListPrendaDocumentPolicies")
            .WithSummary("Lista compañías del OT y si la prenda es opcional")
            .Produces<IReadOnlyList<OtPrendaDocumentPolicyCompanyResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/{tenantId:guid}", SetForOfficeAsync)
            .WithName("AdminOtSetPrendaDocumentPolicy")
            .WithSummary("Activa/desactiva prenda opcional para una compañía en este OT")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListForOfficeAsync(
        Guid transitOfficeId,
        HttpContext httpContext,
        [FromServices] GetOtPrendaDocumentPoliciesHandler handler,
        [FromServices] IOtProfileRepository otProfileRepository,
        CancellationToken cancellationToken)
    {
        var scope = await EnforceTransitOfficeScopeAsync(
            httpContext.User, transitOfficeId, otProfileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (scope is not null)
            return scope;

        var rows = await handler.HandleForOfficeAsync(transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static async Task<IResult> SetForOfficeAsync(
        Guid transitOfficeId,
        Guid tenantId,
        SetOtPrendaDocumentPolicyRequest request,
        HttpContext httpContext,
        [FromServices] SetOtPrendaDocumentPolicyHandler handler,
        [FromServices] IOtProfileRepository otProfileRepository,
        CancellationToken cancellationToken)
    {
        var scope = await EnforceTransitOfficeScopeAsync(
            httpContext.User, transitOfficeId, otProfileRepository, cancellationToken)
            .ConfigureAwait(false);
        if (scope is not null)
            return scope;

        var command = new SetOtPrendaDocumentPolicyCommand
        {
            TenantId = tenantId,
            TransitOfficeId = transitOfficeId,
            DocumentOptional = request.DocumentOptional,
            ChangedBy = ResolveUserId(httpContext.User),
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsValid
            ? Results.NoContent()
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message, value = e.Value }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult?> EnforceTransitOfficeScopeAsync(
        ClaimsPrincipal user,
        Guid transitOfficeId,
        IOtProfileRepository otProfileRepository,
        CancellationToken cancellationToken)
    {
        if (user.IsInRole(AdminAuthorization.SuperAdminRole))
            return null;

        if (Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId))
        {
            var profile = await otProfileRepository
                .GetByTenantAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (profile is not null && profile.TransitOfficeId == transitOfficeId)
                return null;
        }

        return Results.Json(
            new { code = "TRANSIT_OFFICE_FORBIDDEN", message = AdminAuthorization.OtModuleForbiddenMessage },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
