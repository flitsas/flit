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
        // submódulo "Validaciones de Identidad"). No es por-instancia: agrega todas las del tenant.
        group.MapGet("/biometric-validations", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListTenantBiometricValidationsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var result = await handler.HandleAsync(tenantId.Value, ct);
            return Results.Ok(result);
        }).WithName("ListTenantBiometricValidations");

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

        return app;
    }
}

/// <summary>Cuerpo de la simulación de biométrica. <c>parte</c> opcional (vacío → comprador).</summary>
internal sealed record SimularBiometriaRequest(string? Parte);
