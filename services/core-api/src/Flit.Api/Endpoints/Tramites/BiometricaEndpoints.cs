using Flit.Tramites.Application.Identity;
using Flit.Tramites.Application.UseCases.Persons;
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
                    "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede iniciar biométrica en estado borrador."),
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
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede iniciar biométrica en estado borrador."),
                "biometria_activa" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Ya existe una biométrica activa o aprobada para esta parte."),
                _ => Results.Created($"/api/v1/tramites/instances/{id}/biometric/{result!.Validation.Id}", result),
            };
        })
        .WithName("IniciarProcedureInstanceBiometric")
        .Produces<IniciarBiometriaResult>(StatusCodes.Status201Created);

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
        })
        .WithName("ListProcedureInstanceBiometric")
        .Produces<BiometricValidationsResponse>(StatusCodes.Status200OK);

        // GET vista transversal del tenant: TODAS las validaciones de identidad + KPIs (HU #10234,
        // submódulo "Validaciones de Identidad"). Filtros opcionales por columna (HU #10347).
        group.MapGet("/biometric-validations", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromQuery] string? referenceNumber,
            [FromQuery] string? modalidad,
            [FromQuery] string? name,
            [FromQuery] string? partyRole,
            [FromQuery] string? documentType,
            [FromQuery] string? documentNumber,
            [FromQuery] string? status,
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
            // HU #10867 — true = solo standalone; false = solo ligadas a trámite; omitido = todas.
            [FromQuery] bool? standalone,
            ListTenantBiometricValidationsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var query = new TenantBiometricValidationListQuery(
                referenceNumber,
                modalidad,
                name,
                partyRole,
                documentType,
                documentNumber,
                status,
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
                pageSize ?? TenantBiometricValidationListQuery.DefaultPageSize,
                standalone);

            var (result, error) = await handler.HandleAsync(tenantId.Value, query, ct);
            return error is not null
                ? Results.Problem(statusCode: 400, title: "Bad Request", detail: error)
                : Results.Ok(result);
        })
        .WithName("ListTenantBiometricValidations")
        .Produces<TenantBiometricValidationsResponse>(StatusCodes.Status200OK);

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
        })
        .WithName("ListStuckIdentityValidations")
        .Produces<StuckIdentityValidationsResponse>(StatusCodes.Status200OK);

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
        })
        .WithName("SimularProcedureInstanceBiometric")
        .Produces<BiometricValidationDto>(StatusCodes.Status200OK);

        // GET descargar el certificado (PDF) de una validación de identidad desde la API pública de Kyverum.
        // Keyed por validationId ⇒ sirve para comprador o vendedor. 404 si no existe / sin certificado;
        // 502/503 si el proveedor rechaza / no está disponible.
        group.MapGet("/instances/{id:guid}/biometric/{validationId:guid}/certificado", async (
            Guid id,
            Guid validationId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            DescargarCertificadoIdentidadHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, validationId, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Validación de identidad no encontrada."),
                "sin_certificado" => Results.Problem(statusCode: 404, title: "Not Found", detail: "No hay certificado disponible para esta validación."),
                "proveedor_error" => Results.Problem(statusCode: 502, title: "Bad Gateway", detail: "El proveedor rechazó la descarga del certificado."),
                "proveedor_no_disponible" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                _ => Results.File(result!.Content, result.ContentType, result.FileName),
            };
        })
        .WithName("DescargarCertificadoIdentidad")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        // GET bitácora (solo lectura) del ciclo de una validación: envío, llegada del webhook, si descifró el
        // secreto o no, firma, resultado y reconciliaciones. Diagnóstico desde la API sin entrar a la BD/pod.
        group.MapGet("/instances/{id:guid}/biometric/{validationId:guid}/audit", async (
            Guid id,
            Guid validationId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetIdentityAuditHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, validationId, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Validación de identidad no encontrada.")
                : Results.Ok(result);
        })
        .WithName("GetProcedureInstanceIdentityAudit")
        .Produces<IdentityAuditResponse>(StatusCodes.Status200OK);

        // POST reconciliar una validación con Kyverum (fallback si el webhook no llegó): consulta el estado
        // real en Kyverum y lo aplica si ya es terminal. El frontend puede pollear esto en la pantalla de
        // espera. 200 { status, updated }; 404 no existe; 409 no es Kyverum; 502/503 proveedor.
        group.MapPost("/instances/{id:guid}/biometric/{validationId:guid}/reconcile", async (
            Guid id,
            Guid validationId,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ReconciliarIdentidadHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, validationId, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Validación de identidad no encontrada."),
                "no_kyverum" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La validación no es del proveedor Kyverum."),
                "proveedor_error" => Results.Problem(statusCode: 502, title: "Bad Gateway", detail: "El proveedor rechazó la consulta de estado."),
                "proveedor_no_disponible" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                "persist_error" => Results.Problem(statusCode: 500, title: "Internal Server Error", detail: "No se pudo guardar el resultado de la validación. Revisa la bitácora (Historial técnico)."),
                _ => Results.Ok(result),
            };
        })
        .WithName("ReconciliarProcedureInstanceIdentity")
        .Produces<ReconciliarIdentidadResult>(StatusCodes.Status200OK);

        // POST crear prevalidación standalone (HU #10866, CF-01): crea una validación biométrica sin
        // trámite previo. Hace upsert de la entidad Person por (tenant, docType, docNum) y delega
        // la captura al proveedor activo (Kyverum → captureUrl real; mock → magic-link).
        // 201 creada, 202 encolada (fallo transitorio Kyverum), 409 prevalidación activa existente.
        group.MapPost("/biometric-validations", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] IniciarPrevalidacionRequest? body,
            IniciarPrevalidacionHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var (result, error) = await handler.HandleAsync(tenantId.Value, body, ct);
            return error switch
            {
                "datos_incompletos" => Results.Problem(statusCode: 400, title: "Bad Request",
                    detail: "Completa tipo de documento, número de documento, nombre y correo."),
                "datos_representante_requerido" => Results.Problem(statusCode: 400, title: "Bad Request",
                    detail: "Para persona jurídica se requieren tipo, número y nombre del representante legal."),
                "prevalidacion_activa" => Results.Problem(statusCode: 409, title: "Conflict",
                    detail: "Ya existe una prevalidación activa para este documento."),
                "proveedor_error" => Results.Problem(statusCode: 502, title: "Bad Gateway",
                    detail: "El proveedor de validación de identidad rechazó la solicitud."),
                "proveedor_no_disponible" => Results.Problem(statusCode: 503, title: "Service Unavailable",
                    detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                _ when result!.Queued => Results.Accepted(
                    $"/api/v1/tramites/biometric-validations/{result.Validation.Id}", result),
                _ => Results.Created(
                    $"/api/v1/tramites/biometric-validations/{result!.Validation.Id}", result),
            };
        })
        .WithName("IniciarPrevalidacionIdentidad")
        .Produces<IniciarPrevalidacionResult>(StatusCodes.Status201Created)
        .Produces<IniciarPrevalidacionResult>(StatusCodes.Status202Accepted);

        // PATCH editar los datos de contacto de una prevalidación standalone (HU #10943, CF-03, D7):
        // solo nombre/correo (titular) y nombre/correo del representante legal (persona jurídica).
        // Un cambio de correo dispara el reenvío automático en la misma transacción (D8); solo el
        // nombre NO reenvía. Editable ⟺ ProcedureInstanceId IS NULL AND PersonId IS NOT NULL (D12).
        group.MapPatch("/biometric-validations/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            [FromBody] EditarPrevalidacionRequest? body,
            EditarPrevalidacionHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body is null)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el cuerpo de la solicitud.");

            var (result, error, cooldownMinutos) = await handler.HandleAsync(tenantId.Value, id, body, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Prevalidación no encontrada."),
                "no_editable" => Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: "Esta validación pertenece a un trámite; edítala desde el trámite."),
                "identidad_aprobada" => Results.Problem(statusCode: 409, title: "Conflict",
                    detail: "La identidad ya está aprobada. Para revalidar, crea una prevalidación nueva."),
                "referenciada_por_tramite" => Results.Problem(statusCode: 409, title: "Conflict",
                    detail: "Esta prevalidación ya está referenciada por un trámite."),
                "documento_no_editable" => Results.Problem(statusCode: 422, title: "Unprocessable Entity",
                    detail: "El tipo/número de documento no es editable. Anula el registro y crea una prevalidación nueva."),
                "reenvio_en_cooldown" => Results.Problem(statusCode: 429, title: "Too Many Requests",
                    detail: $"Espera {cooldownMinutos} minuto(s) antes de reenviar de nuevo."),
                "tope_reenvios" => Results.Problem(statusCode: 429, title: "Too Many Requests",
                    detail: "Se agotaron los reenvíos disponibles. Anula el registro y crea una prevalidación nueva."),
                "proveedor_error" => Results.Problem(statusCode: 502, title: "Bad Gateway",
                    detail: "El proveedor de validación de identidad rechazó la solicitud."),
                "proveedor_no_disponible" => Results.Problem(statusCode: 503, title: "Service Unavailable",
                    detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                _ => Results.Ok(result),
            };
        })
        .WithName("EditarPrevalidacionIdentidad")
        .Produces<EditarPrevalidacionResult>(StatusCodes.Status200OK);

        // POST reenviar manualmente la validación de identidad de una prevalidación standalone (HU #10943,
        // CF-03, D8): mismo registro, token/enlace nuevo, TTL 24h, intentos/sondeos reiniciados. 200 si el
        // envío se completó; 202 si quedó encolada (falla transitoria del proveedor).
        group.MapPost("/biometric-validations/{id:guid}/resend", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ReenviarPrevalidacionHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error, cooldownMinutos) = await handler.HandleAsync(tenantId.Value, id, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Prevalidación no encontrada."),
                "no_editable" => Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: "Esta validación pertenece a un trámite; reenvíala desde el trámite."),
                "identidad_aprobada" => Results.Problem(statusCode: 409, title: "Conflict",
                    detail: "La identidad ya está aprobada. Para revalidar, crea una prevalidación nueva."),
                "referenciada_por_tramite" => Results.Problem(statusCode: 409, title: "Conflict",
                    detail: "Esta prevalidación ya está referenciada por un trámite."),
                "reenvio_en_cooldown" => Results.Problem(statusCode: 429, title: "Too Many Requests",
                    detail: $"Espera {cooldownMinutos} minuto(s) antes de reenviar de nuevo."),
                "tope_reenvios" => Results.Problem(statusCode: 429, title: "Too Many Requests",
                    detail: "Se agotaron los reenvíos disponibles. Anula el registro y crea una prevalidación nueva."),
                "proveedor_error" => Results.Problem(statusCode: 502, title: "Bad Gateway",
                    detail: "El proveedor de validación de identidad rechazó la solicitud."),
                "proveedor_no_disponible" => Results.Problem(statusCode: 503, title: "Service Unavailable",
                    detail: "El proveedor de validación de identidad no está disponible. Reintenta más tarde."),
                _ when result!.Queued => Results.Accepted(
                    $"/api/v1/tramites/biometric-validations/{id}", result),
                _ => Results.Ok(result),
            };
        })
        .WithName("ReenviarPrevalidacionIdentidad")
        .Produces<ReenviarPrevalidacionResult>(StatusCodes.Status200OK)
        .Produces<ReenviarPrevalidacionResult>(StatusCodes.Status202Accepted);

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
        })
        .WithName("EnsureProcedureInstanceIdentity")
        .Produces<EnsureIdentityResult>(StatusCodes.Status200OK);

        return app;
    }
}

/// <summary>Cuerpo de la simulación de biométrica. <c>parte</c> opcional (vacío → comprador).</summary>
internal sealed record SimularBiometriaRequest(string? Parte);

/// <summary>Cuerpo de "asegurar identidad" (HU #10350). <c>parte</c> = comprador|vendedor.</summary>
internal sealed record EnsureIdentityRequest(string? Parte);
