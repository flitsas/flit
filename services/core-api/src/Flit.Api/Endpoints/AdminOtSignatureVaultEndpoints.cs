using System.Security.Claims;
using Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Baúl de firmas del propio ORGANISMO DE TRÁNSITO. El OT es un tenant y es el dueño del mandatario que registra
/// desde su hub: la firma con baúl de ese mandatario se elige o se captura aquí y vive en el tenant del OT, igual que
/// la del mandatario de una compañía vive en el baúl de la compañía. Solo listar y registrar: es lo que necesita el
/// formulario del mandatario. Mismo alcance que la gestión de mandatarios del hub (Admin OT de ese organismo o Super
/// Admin). Las respuestas NUNCA exponen el material de firma (ADR-0025 §3) y el documento (PII, Ley 1581) no se loguea.
/// </summary>
public static class AdminOtSignatureVaultEndpoints
{
    private const string SinTenantMessage =
        "Este organismo aún no está habilitado en FLIT, así que no tiene baúl de firmas.";

    public static IEndpointRouteBuilder MapAdminOtSignatureVaultEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/signature-vault")
            .RequireAuthorization(AdminAuthorization.OtAdminOrSuperAdminPolicy)
            .AddEndpointFilter<TransitOfficeScopeFilter>()
            .WithTags("Admin OT · Baúl de Firmas");

        group.MapGet("", ListAsync)
            .WithName("AdminOtSignatureVaultList")
            .WithSummary("Lista las firmas del baúl del organismo de tránsito")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("", CreateAsync)
            .WithName("AdminOtSignatureVaultCreate")
            .WithSummary("Registra una firma en el baúl del organismo de tránsito")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid transitOfficeId,
        [FromServices] ITransitOfficeOperationalStatusReader offices,
        [FromServices] ListSignatureVaultHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? documentType = null,
        [FromQuery] string? documentNumber = null,
        [FromQuery] bool? soloVigentes = null)
    {
        var tenantId = await OtTenantAsync(offices, transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (tenantId is null)
        {
            return Results.NotFound(new { error = SinTenantMessage });
        }

        var result = await handler
            .HandleAsync(new ListSignatureVaultQuery
            {
                TenantId = tenantId.Value,
                DocumentType = documentType,
                DocumentNumber = documentNumber,
                SoloVigentes = soloVigentes,
            }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> CreateAsync(
        Guid transitOfficeId,
        CreateSignatureVaultRequest request,
        HttpContext httpContext,
        [FromServices] ITransitOfficeOperationalStatusReader offices,
        [FromServices] CreateSignatureVaultHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenantId = await OtTenantAsync(offices, transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (tenantId is null)
        {
            return Results.NotFound(new { error = SinTenantMessage });
        }

        var result = await handler.HandleAsync(
            new CreateSignatureVaultCommand
            {
                TenantId = tenantId.Value,
                DocumentType = request.DocumentType,
                DocumentNumber = request.DocumentNumber,
                NitEmpresa = request.NitEmpresa,
                FullName = request.FullName,
                VigenciaDesde = request.VigenciaDesde,
                VigenciaHasta = request.VigenciaHasta,
                ArtefactoFirmaBase64 = request.ArtefactoFirmaBase64,
                CodigoHash = request.CodigoHash,
                MandateSignerId = request.MandateSignerId,
                CreatedBy = ResolveUserId(httpContext.User),
            },
            cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/transit-offices/{transitOfficeId}/signature-vault/{result.SignatureVaultId}",
                new { id = result.SignatureVaultId })
            : Results.Json(
                new { errors = result.Errors.Select(e => new { field = e.Field, code = e.Code, message = e.Message }) },
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>Tenant del organismo, o <c>null</c> si el organismo no existe o no tiene tenant en FLIT.</summary>
    private static async Task<Guid?> OtTenantAsync(
        ITransitOfficeOperationalStatusReader offices, Guid transitOfficeId, CancellationToken cancellationToken)
    {
        var office = await offices.GetByIdAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        return office is { HasTenant: true, TenantId: { } tenantId } ? tenantId : null;
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
