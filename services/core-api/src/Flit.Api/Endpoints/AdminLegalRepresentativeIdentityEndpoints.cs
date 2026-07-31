using System.Security.Claims;
using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Companies.LegalRepresentatives;
using Flit.Admin.Domain.Identity;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de la validación de identidad ADMINISTRATIVA de un representante legal (HU #10907,
/// ADR-0034). Módulo Admin Compañías: solo SuperAdmin
/// (<see cref="AdminAuthorization.SuperAdminPolicy"/>). Inician/reenvían por correo una validación
/// DESACOPLADA de un trámite: el endpoint (que conoce el sujeto) arma el descriptor AGNÓSTICO desde el
/// registro del representante y delega en <see cref="IAdminIdentityValidationService"/>. Las respuestas
/// NUNCA exponen el documento ni el correo del representante (PII, Ley 1581).
/// </summary>
public static class AdminLegalRepresentativeIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminLegalRepresentativeIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/legal-representatives/{id:guid}/identity")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Identidad de Representantes");

        // POST /send — inicia la validación (el proveedor notifica el enlace de captura por correo).
        group.MapPost("/send", SendAsync)
            .WithName("AdminLegalRepIdentitySend")
            .WithSummary("Inicia la validación de identidad de un representante legal por correo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // POST /resend — reenvía (respeta la vigencia: no reenvía si ya hay aprobada y vigente).
        group.MapPost("/resend", ResendAsync)
            .WithName("AdminLegalRepIdentityResend")
            .WithSummary("Reenvía la validación de identidad de un representante legal (respeta vigencia)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // POST /link — vincula una identidad que la PERSONA ya validó (HU #11176). NO envía correo ni
        // crea validaciones: 409 si esa persona no tiene ninguna aprobada y vigente en el tenant.
        // Idempotente: si el representante ya la tiene vigente, devuelve reused=true.
        group.MapPost("/link", LinkAsync)
            .WithName("AdminLegalRepIdentityLink")
            .WithSummary("Vincula al representante una validación de identidad ya existente y vigente")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static Task<IResult> SendAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] ILegalRepresentativeReader reader,
        [FromServices] IAdminIdentityValidationService service,
        CancellationToken cancellationToken) =>
        RunAsync(tenantId, id, httpContext, reader, service, resend: false, cancellationToken);

    private static Task<IResult> ResendAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] ILegalRepresentativeReader reader,
        [FromServices] IAdminIdentityValidationService service,
        CancellationToken cancellationToken) =>
        RunAsync(tenantId, id, httpContext, reader, service, resend: true, cancellationToken);

    private static async Task<IResult> LinkAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        [FromServices] ILegalRepresentativeReader reader,
        [FromServices] IAdminIdentityValidationService service,
        CancellationToken cancellationToken)
    {
        var representative = await reader.GetByIdAsync(tenantId, id, cancellationToken).ConfigureAwait(false);
        if (representative is null)
        {
            return Results.NotFound(new { error = $"No existe el representante {id} en esta compañía." });
        }

        var descriptor = new AdminIdentitySubjectDescriptor(
            tenantId,
            AdminIdentitySubjectTypes.LegalRepresentative,
            representative.Id,
            ComposeFullName(representative),
            representative.DocumentType,
            representative.DocumentNumber,
            // Link no necesita correo (no envía nada). Marcador para satisfacer el campo obligatorio
            // del descriptor sin exponer un correo real cuando no existe.
            string.IsNullOrWhiteSpace(representative.Email) ? "sin-correo@flit.local" : representative.Email!,
            ResolveUserId(httpContext.User));

        var result = await service.LinkExistingAsync(descriptor, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            // La persona no tiene ninguna identidad aprobada y vigente en este tenant.
            return Results.Json(
                new { error = "sin_identidad_vigente" },
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> RunAsync(
        Guid tenantId,
        Guid id,
        HttpContext httpContext,
        ILegalRepresentativeReader reader,
        IAdminIdentityValidationService service,
        bool resend,
        CancellationToken cancellationToken)
    {
        var representative = await reader.GetByIdAsync(tenantId, id, cancellationToken).ConfigureAwait(false);
        if (representative is null)
        {
            return Results.NotFound(new { error = $"No existe el representante {id} en esta compañía." });
        }

        if (string.IsNullOrWhiteSpace(representative.Email))
        {
            return Results.Json(
                new { errors = new[] { new { field = "email", code = "email_requerido", message = "El representante no tiene correo para enviar la validación de identidad." } } },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var descriptor = new AdminIdentitySubjectDescriptor(
            tenantId,
            AdminIdentitySubjectTypes.LegalRepresentative,
            representative.Id,
            ComposeFullName(representative),
            representative.DocumentType,
            representative.DocumentNumber,
            representative.Email!,
            ResolveUserId(httpContext.User));

        try
        {
            var result = resend
                ? await service.ResendAsync(descriptor, cancellationToken).ConfigureAwait(false)
                : await service.SendAsync(descriptor, cancellationToken).ConfigureAwait(false);

            var v = result.Validation;
            return Results.Ok(new
            {
                id = v.Id,
                status = v.Status,
                captureUrl = v.CaptureUrl,
                validUntil = v.ValidUntil,
                reused = result.Reused,
            });
        }
        catch (AdminIdentityProviderException ex)
        {
            // Transitorio (proveedor caído/timeout/5xx) → 503; definitivo (4xx) → 502. Sin filtrar secretos.
            var status = ex.Transient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway;
            return Results.Json(
                new { error = ex.Transient ? "proveedor_no_disponible" : "proveedor_error" },
                statusCode: status);
        }
    }

    /// <summary>Respuesta común de las acciones de identidad. NUNCA expone documento ni correo (PII).</summary>
    private static object ToResponse(AdminIdentityValidationResult result) => new
    {
        id = result.Validation.Id,
        status = result.Validation.Status,
        captureUrl = result.Validation.CaptureUrl,
        validUntil = result.Validation.ValidUntil,
        reused = result.Reused,
        provider = result.Validation.Provider,
    };

    /// <summary>Nombre completo del representante para el proveedor (nombres + apellidos).</summary>
    private static string ComposeFullName(LegalRepresentativeItem rep)
    {
        var parts = new[] { rep.Name, rep.FirstLastName, rep.SecondLastName }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        return string.Join(' ', parts);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
