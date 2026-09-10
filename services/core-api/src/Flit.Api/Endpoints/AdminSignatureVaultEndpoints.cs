using System.Security.Claims;
using Flit.Admin.Application.Companies.SignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.CreateSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.GetSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.RevokeSignatureVault;
using Flit.Admin.Application.Companies.SignatureVault.UpdateSignatureVault;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints del baúl de firmas por compañía (ADR-0025 §5, HU #10643, R13/R14). Módulo Admin
/// Compañías: solo SuperAdmin (<see cref="AdminAuthorization.SuperAdminPolicy"/>). Las respuestas
/// NUNCA exponen el material de firma (ADR-0025 §3): solo metadatos + la referencia al artefacto. El
/// número de documento es PII (Ley 1581): nunca se escribe en logs ni en mensajes de error.
/// </summary>
public static class AdminSignatureVaultEndpoints
{
    public static IEndpointRouteBuilder MapAdminSignatureVaultEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/signature-vault")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Baúl de Firmas");

        // GET — firmas del baúl del tenant (activas y revocadas), sin material de firma.
        // Filtros opcionales: documentType + documentNumber (AC1, HU #11175) y soloVigentes (AC2).
        group.MapGet("", ListAsync)
            .WithName("AdminSignatureVaultList")
            .WithSummary("Lista las firmas del baúl de una compañía")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        // GET /{id} — una firma por id.
        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("AdminSignatureVaultGetById")
            .WithSummary("Obtiene una firma del baúl por id")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        // POST — alta de firma (sube el artefacto a storage; 422 si ya hay activa para el documento).
        group.MapPost("", CreateAsync)
            .WithName("AdminSignatureVaultCreate")
            .WithSummary("Registra una firma precargada en el baúl de la compañía")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // PUT /{id} — corrección de los datos capturados de una firma ACTIVA.
        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("AdminSignatureVaultUpdate")
            .WithSummary("Corrige los datos de una firma del baúl")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // POST /{id}/revoke — baja lógica idempotente.
        group.MapPost("/{id:guid}/revoke", RevokeAsync)
            .WithName("AdminSignatureVaultRevoke")
            .WithSummary("Revoca una firma del baúl (idempotente)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        [FromServices] ListSignatureVaultHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] string? documentType = null,
        [FromQuery] string? documentNumber = null,
        [FromQuery] bool? soloVigentes = null)
    {
        var result = await handler
            .HandleAsync(new ListSignatureVaultQuery
            {
                TenantId = tenantId,
                DocumentType = documentType,
                DocumentNumber = documentNumber,
                SoloVigentes = soloVigentes,
            }, cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { data = result });
    }

    private static async Task<IResult> GetByIdAsync(
        Guid tenantId,
        Guid id,
        [FromServices] GetSignatureVaultByIdHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(new GetSignatureVaultByIdQuery { TenantId = tenantId, Id = id }, cancellationToken)
            .ConfigureAwait(false);

        return result is null
            ? Results.NotFound(new { error = $"No existe la firma {id} en el baúl de esta compañía." })
            : Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateSignatureVaultRequest request,
        HttpContext httpContext,
        [FromServices] CreateSignatureVaultHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateSignatureVaultCommand
        {
            TenantId = tenantId,
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
        };

        var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsValid
            ? Results.Created(
                $"/api/v1/admin/companies/{tenantId}/signature-vault/{result.SignatureVaultId}",
                new { id = result.SignatureVaultId })
            : ValidationProblem(result.Errors);
    }

    private static async Task<IResult> RevokeAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] RevokeSignatureVaultHandler handler,
        CancellationToken cancellationToken)
    {
        var outcome = await handler
            .HandleAsync(
                new RevokeSignatureVaultCommand
                {
                    TenantId = tenantId,
                    Id = id,
                    ChangedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return outcome == RevokeSignatureVaultOutcome.Revoked
            ? Results.NoContent()
            : Results.NotFound(new { error = $"No existe la firma {id} en el baúl de esta compañía." });
    }

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    /// <summary>
    /// Corrige los datos capturados de una firma. No admite cambiar el documento (identifica a la
    /// persona) ni el artefacto (lo ya emitido se estampó con esa imagen): para eso se captura una
    /// firma nueva, que revoca la anterior conservándola.
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        Guid tenantId,
        Guid id,
        UpdateSignatureVaultRequest request,
        HttpContext httpContext,
        [FromServices] UpdateSignatureVaultHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler.HandleAsync(
            new UpdateSignatureVaultCommand
            {
                TenantId = tenantId,
                Id = id,
                FullName = request.FullName,
                CodigoHash = request.CodigoHash,
                VigenciaDesde = request.VigenciaDesde,
                VigenciaHasta = request.VigenciaHasta,
                ChangedBy = ResolveUserId(httpContext.User),
            },
            cancellationToken).ConfigureAwait(false);

        return result.Outcome switch
        {
            UpdateSignatureVaultOutcome.Updated => Results.NoContent(),
            UpdateSignatureVaultOutcome.NotFound =>
                Results.NotFound(new { error = $"No existe la firma {id} en el baúl de esta compañía." }),
            UpdateSignatureVaultOutcome.Revoked => Results.Conflict(
                new { error = "La firma está revocada: su contenido es histórico y no se corrige." }),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static IResult ValidationProblem(IReadOnlyList<SignatureVaultValidationError> errors) =>
        Results.Json(
            new { errors = errors.Select(e => new { field = e.Field, code = e.Code, message = e.Message }) },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>Cuerpo de la edición: solo los campos corregibles.</summary>
    public sealed record UpdateSignatureVaultRequest(
        string? FullName,
        string? CodigoHash,
        DateOnly VigenciaDesde,
        DateOnly VigenciaHasta);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
