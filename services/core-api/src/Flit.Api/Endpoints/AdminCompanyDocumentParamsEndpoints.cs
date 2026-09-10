using System.Security.Claims;
using Flit.Admin.Application.CompanyDocumentParams;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Parámetros documentales por compañía gestora (HU #10521, RF31): estado
/// OCULTO/OBLIGATORIO/OPCIONAL por tipo de documento. Grupo bajo rol SuperAdmin.
/// </summary>
public static class AdminCompanyDocumentParamsEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompanyDocumentParamsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/document-params")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Documentos");

        group.MapGet("/", ListAsync)
            .WithName("AdminCompanyDocumentParamsList")
            .WithSummary("Lista los parámetros documentales de una gestora")
            .Produces<IReadOnlyList<CompanyDocumentParamResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPut("/", UpsertAsync)
            .WithName("AdminCompanyDocumentParamsUpsert")
            .WithSummary("Fija el estado documental de un tipo para la gestora (upsert)")
            .Produces<CompanyDocumentParamResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        [FromServices] ListCompanyDocumentParamsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpsertAsync(
        Guid tenantId,
        UpsertCompanyDocumentParamRequest request,
        HttpContext httpContext,
        [FromServices] UpsertCompanyDocumentParamHandler handler,
        CancellationToken cancellationToken)
    {
        var (result, error) = await handler
            .HandleAsync(tenantId, request, ResolveUserId(httpContext.User), cancellationToken)
            .ConfigureAwait(false);

        return error switch
        {
            "invalid_code" => Results.Json(
                new { error = "El código de documento es obligatorio." },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            "invalid_state" => Results.Json(
                new { error = "Estado inválido (use OCULTO, OBLIGATORIO u OPCIONAL)." },
                statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Ok(result),
        };
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
