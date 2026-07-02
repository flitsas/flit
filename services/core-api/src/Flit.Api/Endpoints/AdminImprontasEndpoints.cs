using System.Security.Claims;
using Flit.Admin.Application.Improntas.GenerarImpronta;
using Flit.Api.Authorization;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de generación de improntas vehiculares (HU #10467, Feature #10462) — integración
/// Kyverum RUNT (HU #10465) + persistencia de historial (HU #10466). Acceso exclusivo SuperAdmin
/// (AC2): solo hoy existe el paso de generación; el listado/historial (HU #10468) se agrega en el
/// mismo grupo cuando se implemente.
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
