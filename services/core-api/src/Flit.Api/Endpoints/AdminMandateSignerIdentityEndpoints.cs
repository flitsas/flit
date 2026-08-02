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

        // POST /link — vincula una identidad que la PERSONA ya validó (HU #11028). NO envía correo ni
        // crea validaciones: 409 si esa persona no tiene ninguna aprobada y vigente.
        group.MapPost("/link", LinkAsync)
            .WithName("AdminMandateSignerIdentityLink")
            .WithSummary("Vincula al mandatario una validación de identidad ya existente y vigente")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        // POST /mock — SIMULA una validación aprobada (HU #11028). Solo en ambientes con la simulación
        // habilitada por configuración; en cualquier otro responde 403. Es el mecanismo para probar la
        // firma del mandato donde nadie puede completar una biométrica real.
        group.MapPost("/mock", MockAsync)
            .WithName("AdminMandateSignerIdentityMock")
            .WithSummary("Simula una validación de identidad aprobada (solo ambientes de prueba)")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static Task<IResult> SendAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        [FromServices] IMandateSignerReader reader,
        [FromServices] ITransitOfficeOperationalStatusReader otStatus,
        [FromServices] IAdminIdentityValidationService service,
        [FromServices] AdminIdentityMockOptions mockOptions,
        CancellationToken cancellationToken) =>
        RunAsync(transitOfficeId, id, httpContext, reader, otStatus, service, mockOptions, resend: false, cancellationToken);

    private static Task<IResult> ResendAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        [FromServices] IMandateSignerReader reader,
        [FromServices] ITransitOfficeOperationalStatusReader otStatus,
        [FromServices] IAdminIdentityValidationService service,
        [FromServices] AdminIdentityMockOptions mockOptions,
        CancellationToken cancellationToken) =>
        RunAsync(transitOfficeId, id, httpContext, reader, otStatus, service, mockOptions, resend: true, cancellationToken);

    private static async Task<IResult> LinkAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        [FromServices] IMandateSignerReader reader,
        [FromServices] ITransitOfficeOperationalStatusReader otStatus,
        [FromServices] IAdminIdentityValidationService service,
        CancellationToken cancellationToken)
    {
        var (descriptor, error) = await BuildDescriptorAsync(
            transitOfficeId, id, httpContext, reader, otStatus, requireEmail: false, cancellationToken)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var result = await service.LinkExistingAsync(descriptor!, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            // Nada que vincular: la persona no tiene identidad aprobada y vigente en este tenant.
            return Results.Json(
                new { error = "sin_identidad_vigente" },
                statusCode: StatusCodes.Status409Conflict);
        }

        return Results.Ok(ToResponse(result));
    }

    private static async Task<IResult> MockAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        [FromServices] IMandateSignerReader reader,
        [FromServices] ITransitOfficeOperationalStatusReader otStatus,
        [FromServices] IAdminIdentityValidationService service,
        [FromServices] AdminIdentityMockOptions mockOptions,
        CancellationToken cancellationToken)
    {
        // Guarda de ambiente ANTES de tocar nada: una identidad simulada satisface el gate que habilita
        // la firma del mandato, así que fuera de un ambiente de prueba esto no debe existir.
        if (!mockOptions.Enabled)
        {
            return Results.Json(
                new { error = "simulacion_deshabilitada" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var (descriptor, error) = await BuildDescriptorAsync(
            transitOfficeId, id, httpContext, reader, otStatus, requireEmail: false, cancellationToken)
            .ConfigureAwait(false);
        if (error is not null)
        {
            return error;
        }

        var result = await service.SimulateApprovedAsync(descriptor!, cancellationToken).ConfigureAwait(false);
        return Results.Ok(ToResponse(result));
    }

    /// <summary>
    /// Arma el descriptor AGNÓSTICO del mandatario validando OT y alta de tenant. <paramref name="requireEmail"/>
    /// distingue las acciones que mandan correo (send/resend) de las que no (link/mock): vincular una
    /// identidad ya validada no necesita buzón.
    /// </summary>
    internal static async Task<(AdminIdentitySubjectDescriptor? Descriptor, IResult? Error)> BuildDescriptorAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        IMandateSignerReader reader,
        ITransitOfficeOperationalStatusReader otStatus,
        bool requireEmail,
        CancellationToken cancellationToken)
    {
        var signer = await reader.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (signer is null || signer.TransitOfficeId != transitOfficeId)
        {
            return (null, Results.NotFound(new { error = $"No existe el mandatario {id} en este organismo." }));
        }

        if (requireEmail && string.IsNullOrWhiteSpace(signer.Email))
        {
            return (null, Results.Json(
                new { errors = new[] { new { field = "email", code = "email_requerido", message = "El mandatario no tiene correo para enviar la validación de identidad." } } },
                statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var status = await otStatus.GetByIdAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (status is null || !status.HasTenant || status.TenantId is null)
        {
            return (null, Results.Json(new { error = "ot_sin_alta" }, statusCode: StatusCodes.Status422UnprocessableEntity));
        }

        var descriptor = new AdminIdentitySubjectDescriptor(
            status.TenantId.Value,
            AdminIdentitySubjectTypes.MandateSigner,
            signer.Id,
            signer.FullName,
            signer.DocumentType,
            signer.DocumentNumber,
            // Sin correo (link/mock) se usa un marcador: el agregado exige el campo pero no se notifica a nadie.
            string.IsNullOrWhiteSpace(signer.Email) ? "sin-correo@flit.local" : signer.Email!,
            ResolveUserId(httpContext.User),
            transitOfficeId);

        return (descriptor, null);
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

    private static async Task<IResult> RunAsync(
        Guid transitOfficeId,
        Guid id,
        HttpContext httpContext,
        IMandateSignerReader reader,
        ITransitOfficeOperationalStatusReader otStatus,
        IAdminIdentityValidationService service,
        AdminIdentityMockOptions mockOptions,
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
            ResolveUserId(httpContext.User),
            transitOfficeId);

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
            // HU #11028 — en un ambiente de PRUEBA no hay proveedor con el que validar de verdad (sin
            // API key, Kyverum responde error), así que el envío caía en 502 y no había manera de dejar
            // al mandatario validado. Con la simulación habilitada se cae a una validación simulada:
            // el objetivo del ambiente es poder probar la firma del mandato, no el correo.
            if (mockOptions.Enabled)
            {
                var simulada = await service.SimulateApprovedAsync(descriptor, cancellationToken).ConfigureAwait(false);
                return Results.Ok(ToResponse(simulada));
            }

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
