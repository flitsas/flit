using System.Security.Claims;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Confirm;
using Flit.Admin.Application.Companies.PersonalizedDocuments.Create;
using Flit.Admin.Application.Companies.PersonalizedDocuments.List;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de documentos personalizados por compañía (HU #11313, Feature #11309, ADR-0042). Módulo
/// Admin Compañías: <see cref="AdminAuthorization.AdminCompanyPolicy"/> +
/// <see cref="CompanyOwnTenantFilter"/> — el mismo par que protege las escrituras (ADR-0033), sin
/// política nueva. <b>Regla de firma no negociable</b>: <c>Guid tenantId</c> va SIEMPRE primero en la
/// firma del handler — <see cref="CompanyOwnTenantFilter"/> toma el primer <c>Guid</c> de los
/// argumentos del endpoint para autorizar; invertir el orden autoriza contra el id equivocado.
/// Las rutas de escritura (POST, confirm) responden 409 <c>canal_no_habilitado</c> si el canal del
/// tenant no es <c>TENANT_API</c>; el listado (GET) no lo exige (el historial se sigue pudiendo
/// consultar tras volver a <c>FLIT_SMTP</c>).
/// </summary>
public static class AdminPersonalizedDocumentsEndpoints
{
    public static IEndpointRouteBuilder MapAdminPersonalizedDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/personalized-documents")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Documentos personalizados");

        group.MapGet("", ListAsync)
            .WithName("AdminPersonalizedDocumentsList")
            .WithSummary("Lista el historial de documentos personalizados de una compañía, agrupado por tipo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("", CreateAsync)
            .WithName("AdminPersonalizedDocumentsCreate")
            .WithSummary("Registra una nueva versión de documento personalizado (mandato | tramite_virtual)")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/confirm", ConfirmAsync)
            .WithName("AdminPersonalizedDocumentsConfirm")
            .WithSummary("Confirma una versión: recalcula el SHA-256 y valida la integridad del PDF")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        [FromServices] ListPersonalizedDocumentsHandler handler,
        CancellationToken cancellationToken)
    {
        var documents = await handler.HandleAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { documents });
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreatePersonalizedDocumentRequest request,
        HttpContext httpContext,
        [FromServices] CreatePersonalizedDocumentVersionHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await handler
            .HandleAsync(
                new CreatePersonalizedDocumentVersionCommand
                {
                    TenantId = tenantId,
                    DocumentType = request.DocumentType,
                    Filename = request.Filename,
                    Sha256 = request.Sha256,
                    SizeBytes = request.SizeBytes,
                    CreatedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            CreatePersonalizedDocumentVersionOutcome.Created => Results.Created(
                $"/api/v1/admin/companies/{tenantId}/personalized-documents/{result.Id}",
                new
                {
                    id = result.Id,
                    version = result.Version,
                    upload = ToUpload(result.Upload!),
                }),
            CreatePersonalizedDocumentVersionOutcome.InvalidDocumentType => Results.BadRequest(
                new { error = "tipo_documento_invalido", message = result.Errors[0].Message }),
            CreatePersonalizedDocumentVersionOutcome.ChannelNotEnabled => ChannelNotEnabledResult(),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static async Task<IResult> ConfirmAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] ConfirmPersonalizedDocumentVersionHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler
            .HandleAsync(
                new ConfirmPersonalizedDocumentVersionCommand
                {
                    TenantId = tenantId,
                    Id = id,
                    ConfirmedBy = ResolveUserId(httpContext.User),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            ConfirmPersonalizedDocumentVersionOutcome.Activated => Results.Ok(new
            {
                id,
                version = result.Version,
                status = "activo",
                sha256 = result.Sha256,
                pageCount = result.PageCount,
            }),
            ConfirmPersonalizedDocumentVersionOutcome.NotFound => Results.NotFound(
                new { error = $"No existe la versión {id} en esta compañía." }),
            ConfirmPersonalizedDocumentVersionOutcome.ChannelNotEnabled => ChannelNotEnabledResult(),
            _ => ValidationProblem(result.Errors),
        };
    }

    private static object ToUpload(Flit.Admin.Application.Companies.PersonalizedDocuments.PersonalizedDocumentUploadTicket ticket) =>
        new { storagePath = ticket.StoragePath, url = ticket.Url, fields = ticket.Fields };

    /// <summary>422 con el sobre estándar de errores; nunca incluye PII.</summary>
    private static IResult ValidationProblem(
        IReadOnlyList<Flit.Admin.Application.Companies.PersonalizedDocuments.PersonalizedDocumentValidationError> errors) =>
        Results.Json(
            new { errors = errors.Select(e => new { field = e.Field, code = e.Code, message = e.Message }) },
            statusCode: StatusCodes.Status422UnprocessableEntity);

    /// <summary>409 — el canal del tenant no es TENANT_API (§8 DT-7 del plan técnico).</summary>
    private static IResult ChannelNotEnabledResult() =>
        Results.Json(
            new
            {
                error = "canal_no_habilitado",
                message = "Esta funcionalidad requiere que el canal del tenant sea TENANT_API.",
            },
            statusCode: StatusCodes.Status409Conflict);

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

/// <summary>Payload de alta de una versión (el binario se sube después, con el ticket devuelto).</summary>
public sealed class CreatePersonalizedDocumentRequest
{
    public string? DocumentType { get; init; }
    public string? Filename { get; init; }
    public string? Sha256 { get; init; }
    public long SizeBytes { get; init; }
}
