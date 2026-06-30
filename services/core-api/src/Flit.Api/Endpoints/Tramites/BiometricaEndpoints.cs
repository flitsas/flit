using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class BiometricaEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesBiometricaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // POST iniciar biométrica de una parte. Según el flag Biometrics:Provider (AC4):
        //  - mock     -> 201 { validation, token, magicLinkPath } (flujo Slice 6, magic-link 3 fotos)
        //  - kyverum  -> 201 { validation, captureUrl } (HU #10233, captura remota + webhook)
        group.MapPost("/instances/{id:guid}/biometric", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] IniciarBiometriaInput? body,
            BiometricsProviderOptions providerOptions,
            IniciarBiometriaHandler mockHandler,
            IniciarKyverumVerifyHandler kyverumHandler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            if (providerOptions.IsKyverum)
            {
                var (kResult, kError) = await kyverumHandler.HandleAsync(id, tenantId.Value, body, ct);
                return kError switch
                {
                    "datos_incompletos" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Completa nombre, tipo de documento, documento y email."),
                    "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor o vacío)."),
                    "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                    "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede iniciar biométrica en estado draft."),
                    "actor_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Captura el actor de la parte antes de iniciar la validación de identidad."),
                    "biometria_activa" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe una biométrica activa o aprobada para esta parte."),
                    "proveedor_error" => Results.Problem(statusCode: 502, title: "Bad Gateway", detail: "El proveedor de validación de identidad rechazó la solicitud."),
                    "proveedor_no_disponible" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                    // Encolado (fallo transitorio del proveedor): 202 Accepted — el worker reintentará el envío.
                    _ when kResult!.Queued => Results.Accepted($"/api/v1/tramites/instances/{id}/biometric/{kResult.Validation.Id}", kResult),
                    _ => Results.Created($"/api/v1/tramites/instances/{id}/biometric/{kResult!.Validation.Id}", kResult),
                };
            }

            var (result, error) = await mockHandler.HandleAsync(id, tenantId.Value, body, ct);
            return error switch
            {
                "datos_incompletos" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Completa nombre, tipo de documento, documento y email."),
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor o vacío)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede iniciar biométrica en estado draft."),
                "biometria_activa" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe una biométrica activa o aprobada para esta parte."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/biometric/{result!.Validation.Id}", result),
            };
        }).WithName("IniciarProcedureInstanceBiometric");

        // GET lista/estado de biométricas -> { validations: [...] }
        group.MapGet("/instances/{id:guid}/biometric", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListBiometriaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("ListProcedureInstanceBiometric");

        // GET vista transversal del tenant: TODAS las validaciones de identidad + KPIs (HU #10234,
        // submódulo "Validaciones de Identidad"). Filtros opcionales por columna (HU #10347).
        group.MapGet("/biometric-validations", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromQuery] string? referenceNumber,
            [FromQuery] string? modalidad,
            [FromQuery] string? nombre,
            [FromQuery] string? parte,
            [FromQuery] string? tipoDoc,
            [FromQuery] string? documento,
            [FromQuery] string? estado,
            [FromQuery] string? provider,
            [FromQuery] int? scoreMin,
            [FromQuery] int? scoreMax,
            [FromQuery] DateTimeOffset? createdFrom,
            [FromQuery] DateTimeOffset? createdTo,
            [FromQuery] string? motivoRechazo,
            [FromQuery] string? vigenciaEstado,
            [FromQuery] DateTimeOffset? expiraDesde,
            [FromQuery] DateTimeOffset? expiraHasta,
            [FromQuery] int? venceEnDias,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            ListTenantBiometricValidationsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var query = new TenantBiometricValidationListQuery(
                referenceNumber,
                modalidad,
                nombre,
                parte,
                tipoDoc,
                documento,
                estado,
                provider,
                scoreMin,
                scoreMax,
                createdFrom,
                createdTo,
                motivoRechazo,
                vigenciaEstado,
                expiraDesde,
                expiraHasta,
                venceEnDias,
                page ?? 1,
                pageSize ?? TenantBiometricValidationListQuery.DefaultPageSize);

            var (result, error) = await handler.HandleAsync(tenantId.Value, query, ct);
            return error is not null
                ? Results.Problem(statusCode: 400, title: "Bad Request", detail: error)
                : Results.Ok(result);
        }).WithName("ListTenantBiometricValidations");

        // GET eventos de validación de identidad ATASCADOS (dead-letter): pendientes que agotaron los
        // reintentos del worker de outbox (fase 2). Observabilidad para reencolar manualmente.
        group.MapGet("/identity-validation/stuck", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListStuckIdentityValidationsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var result = await handler.HandleAsync(tenantId.Value, ct);
            return Results.Ok(result);
        }).WithName("ListStuckIdentityValidations");

        // POST reencolar ("desatascar") un evento de identidad atascado: reinicia sus intentos para que el
        // worker lo vuelva a procesar. 404 si no hay un evento atascado con ese id para el tenant.
        group.MapPost("/identity-validation/stuck/{id:guid}/requeue", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RequeueStuckIdentityValidationHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var error = await handler.HandleAsync(tenantId.Value, id, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "No hay un evento atascado con ese id.")
                : Results.Ok(new { requeued = true });
        }).WithName("RequeueStuckIdentityValidation");

        // POST reencolar TODOS los eventos atascados del tenant de una vez → { requeued: N }.
        group.MapPost("/identity-validation/stuck/requeue-all", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RequeueAllStuckIdentityValidationsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var count = await handler.HandleAsync(tenantId.Value, ct);
            return Results.Ok(new { requeued = count });
        }).WithName("RequeueAllStuckIdentityValidations");

        // POST simular biométrica (mock, sin fotos) -> 200 BiometricValidationDto aprobada.
        group.MapPost("/instances/{id:guid}/biometric/simulate", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] SimularBiometriaRequest? body,
            SimularBiometriaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, body?.Parte, ct);
            return error switch
            {
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor o vacío)."),
                "instance_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "actor_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Captura el actor de la parte antes de simular la biométrica."),
                _ => Results.Ok(result),
            };
        }).WithName("SimularProcedureInstanceBiometric");

        // POST asegurar identidad de una parte (HU #10350): reutiliza una validación vigente de la
        // persona (clonándola) o responde que requiere validación, para que el front la dispare sin clic.
        group.MapPost("/instances/{id:guid}/identity/ensure", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] EnsureIdentityRequest? body,
            EnsureIdentityHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, body?.Parte, ct);
            return error switch
            {
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                _ => Results.Ok(result),
            };
        }).WithName("EnsureProcedureInstanceIdentity");

        return app;
    }
}

/// <summary>Cuerpo de la simulación de biométrica. <c>parte</c> opcional (vacío → comprador).</summary>
internal sealed record SimularBiometriaRequest(string? Parte);

/// <summary>Cuerpo de "asegurar identidad" (HU #10350). <c>parte</c> = comprador|vendedor.</summary>
internal sealed record EnsureIdentityRequest(string? Parte);
