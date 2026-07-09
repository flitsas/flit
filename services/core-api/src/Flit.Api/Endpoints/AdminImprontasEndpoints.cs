using System.Security.Claims;
using Flit.Admin.Application.Improntas.GenerarImpronta;
using Flit.Admin.Application.Improntas.ListImprontas;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de improntas vehiculares (Feature #10462): generación PDF (HU #10467, Kyverum RUNT
/// HU #10465 + persistencia HU #10466) y listado paginado del historial (HU #10468). Acceso
/// exclusivo SuperAdmin.
/// </summary>
public static class AdminImprontasEndpoints
{
    public static IEndpointRouteBuilder MapAdminImprontasEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/improntas")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Improntas");

        group.MapGet("", ListImprontasAsync)
            .WithName("AdminImprontasIndex")
            .WithSummary("Lista el historial de improntas generadas (paginado)")
            .WithDescription("Listado paginado y filtrable (placa, radicado, rango de fecha de creación) "
                + "del historial de improntas generadas (admin.impronta_generations), ordenado por fecha "
                + "de creación descendente. Vista global cross-tenant (sin RLS, ADR-0022): no re-expone el "
                + "PDF, solo metadata. Requiere SuperAdmin.")
            .Produces<ListImprontasResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("/generate", GenerateAsync)
            .WithName("AdminImprontasGenerate")
            .WithSummary("Genera, persiste y entrega el PDF del Certificado de Improntas Digitales")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway);

        return app;
    }

    private static async Task<IResult> ListImprontasAsync(
        [FromServices] ListImprontasHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? placa = null,
        [FromQuery] string? radicado = null,
        [FromQuery] DateTimeOffset? createdFrom = null,
        [FromQuery] DateTimeOffset? createdTo = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var query = new ListImprontasQuery
        {
            Placa = placa,
            Radicado = radicado,
            CreatedFrom = createdFrom,
            CreatedTo = createdTo,
            Page = page,
            PageSize = pageSize,
        };

        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    private static async Task<IResult> GenerateAsync(
        HttpContext httpContext,
        GenerarImprontaRequest request,
        GenerarImprontaHandler handler,
        CancellationToken cancellationToken)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var flitUserId = ResolveUserId(httpContext.User);
        if (flitUserId is null)
        {
            return Results.Json(
                new { error = "Token inválido: falta claim sub" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await handler.HandleAsync(
            new GenerarImprontaCommand
            {
                Request = request,
                TenantId = tenantId,
                FlitUserId = flitUserId.Value,
            },
            cancellationToken).ConfigureAwait(false);

        switch (result.Status)
        {
            case GenerarImprontaStatus.ValidationFailed:
                return Results.Json(
                    new { errors = result.Errors.Select(e => new { field = e.Field, message = e.Message }) },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            case GenerarImprontaStatus.ProviderValidationFailed:
                return Results.Json(
                    new { error = result.ProviderErrorCode, message = result.ProviderMessage },
                    statusCode: StatusCodes.Status422UnprocessableEntity);

            case GenerarImprontaStatus.ProviderUnauthorized:
                return Results.Json(
                    new { error = result.ProviderErrorCode, message = result.ProviderMessage },
                    statusCode: StatusCodes.Status401Unauthorized);

            case GenerarImprontaStatus.ProviderUnavailable:
                return Results.Json(
                    new { error = result.ProviderErrorCode, message = result.ProviderMessage },
                    statusCode: StatusCodes.Status502BadGateway);

            default:
                var fileName = $"impronta_{result.Radicado}.pdf";
                return Results.File(result.PdfBytes!, "application/pdf", fileName);
        }
    }

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
