using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners;
using Flit.Admin.Application.Identity;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Endpoints de la validación de identidad ADMINISTRATIVA de un mandatario (ADR-0036, HU #10911;
/// reutiliza el bloque agnóstico de ADR-0034). Módulo Admin OT: SuperAdmin u ot_admin
/// (<see cref="AdminAuthorization.OtModulePolicy"/>). Inician/reenvían por correo una validación
/// DESACOPLADA de un trámite: el endpoint arma el descriptor AGNÓSTICO desde el registro del
/// mandatario (<c>subject_type = 'mandate_signer'</c>) y delega en
/// <see cref="IAdminIdentityValidationService"/>. La validación se ancla al tenant del OT. Las
/// respuestas NUNCA exponen el documento ni el correo del mandatario (PII, Ley 1581).
/// </summary>
public static class AdminMandateSignerIdentityEndpoints
{
    public static IEndpointRouteBuilder MapAdminMandateSignerIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/transit-offices/{transitOfficeId:guid}/mandate-signers/{id:guid}/identity")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · Identidad de Mandatarios");

        // POST /send — inicia la validación (el proveedor notifica el enlace de captura por correo).
        group.MapPost("/send", SendAsync)
            .WithName("AdminMandateSignerIdentitySend")
            .WithSummary("Inicia la validación de identidad de un mandatario por correo")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // POST /resend — reenvía (respeta la vigencia: no reenvía si ya hay aprobada y vigente).
        group.MapPost("/resend", ResendAsync)
            .WithName("AdminMandateSignerIdentityResend")
            .WithSummary("Reenvía la validación de identidad de un mandatario (respeta vigencia)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static Task<IResult> SendAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        [FromServices] IMandateSignerReader reader,
        [FromServices] ITransitOfficeOperationalStatusReader otStatus,
        [FromServices] IAdminIdentityValidationService service,
        CancellationToken cancellationToken) =>
        RunAsync(transitOfficeId, id, httpContext, reader, otStatus, service, resend: false, cancellationToken);

    private static Task<IResult> ResendAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        [FromServices] IMandateSignerReader reader,
        [FromServices] ITransitOfficeOperationalStatusReader otStatus,
        [FromServices] IAdminIdentityValidationService service,
        CancellationToken cancellationToken) =>
        RunAsync(transitOfficeId, id, httpContext, reader, otStatus, service, resend: true, cancellationToken);

    private static async Task<IResult> RunAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        IMandateSignerReader reader,
        ITransitOfficeOperationalStatusReader otStatus,
        IAdminIdentityValidationService service,
        bool resend,
        CancellationToken cancellationToken)
    {
        var signer = await reader.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (signer is null || signer.TransitOfficeId != transitOfficeId)
        {
            return Results.NotFound(new { error = $"No existe el mandatario {id} en este organismo." });
        }

        if (string.IsNullOrWhiteSpace(signer.Email))
        {
            return Results.Json(
                new { errors = new[] { new { field = "email", code = "email_requerido", message = "El mandatario no tiene correo para enviar la validación de identidad." } } },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // La validación de identidad admin es tenant-scoped: se ancla al tenant del OT.
        var status = await otStatus.GetByIdAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (status is null || !status.HasTenant || status.TenantId is null)
        {
            return Results.Json(
                new { error = "ot_sin_alta" },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var descriptor = new AdminIdentitySubjectDescriptor(
            status.TenantId.Value,
            AdminIdentitySubjectTypes.MandateSigner,
            signer.Id,
            signer.FullName,
            signer.DocumentType,
            signer.DocumentNumber,
            signer.Email!,
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
            var httpStatus = ex.Transient ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status502BadGateway;
            return Results.Json(
                new { error = ex.Transient ? "proveedor_no_disponible" : "proveedor_error" },
                statusCode: httpStatus);
        }
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
