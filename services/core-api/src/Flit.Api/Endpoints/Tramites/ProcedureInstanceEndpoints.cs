using System.Security.Claims;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Api.Middleware;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

internal static class ProcedureInstanceEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances", async (
            CreateProcedureInstanceRequest request,
            HttpContext http,
            CreateProcedureInstanceHandler handler,
            GetTenantSettingsHandler settingsHandler,
            CancellationToken ct) =>
        {
            // #1 — El tenant y el usuario creador SALEN del JWT, no del body (no se confía en el
            // cliente). Un usuario de compañía siempre crea en SU compañía; el superadmin debe
            // indicar la compañía destino (header X-Tenant-Id o body).
            var (resolvedTenant, isSuperAdmin) = ResolveTenantContext(http);
            Guid effectiveTenant;
            if (isSuperAdmin)
            {
                effectiveTenant = resolvedTenant ?? request.TenantId;
                if (effectiveTenant == Guid.Empty)
                    return Results.Problem(statusCode: 400, title: "Bad Request",
                        detail: "Indique la compañía destino (X-Tenant-Id) para crear el trámite.");
            }
            else if (resolvedTenant is { } companyTenant)
            {
                effectiveTenant = companyTenant;
            }
            else
            {
                return Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: "El usuario autenticado no tiene una compañía asignada.");
            }

            var effectiveRequest = request with
            {
                TenantId = effectiveTenant,
                CreatedByUserId = ResolveUserId(http.User) ?? request.CreatedByUserId,
            };

            // #5 — La compañía debe habilitar explícitamente la matrícula inicial vía el
            // toggle "Permitir matrícula inicial" (admin/companies). Por defecto está en OFF:
            // solo se permite crear ese trámite si existe configuración del tenant Y el flag
            // está en true. Sin fila de settings (tenant no configurado) → NO permitido, para
            // que una compañía sin configuración no radique matrícula inicial por accidente.
            if (EsMatriculaInicial(effectiveRequest.Modalidad))
            {
                var settings = await settingsHandler.HandleAsync(
                    new GetTenantSettingsQuery { TenantId = effectiveRequest.TenantId }, ct);
                if (settings is not { SwitchesMatricula.AllowInitialRegistration: true })
                    return Results.Problem(
                        statusCode: 422,
                        title: "Unprocessable Entity",
                        detail: "La compañía no tiene habilitada la matrícula inicial. Contacta al administrador para activarla.");
            }

            var (result, error) = await handler.HandleAsync(effectiveRequest, ct);
            return error switch
            {
                "invalid_request" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Debe indicar exactamente uno de procedureTypeId o modalidad."),
                "modalidad_not_available" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay un tipo de trámite publicado para la modalidad indicada."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                "invalid_reference" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El tenant, el usuario o el tipo de trámite indicado no existe."),
                "reference_conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se pudo generar un número de referencia único. Reintente."),
                _ => Results.Created($"/api/v1/tramites/instances/{result!.Id}", result)
            };
        }).WithName("CreateProcedureInstance");

        // Listado para la tabla de operación (Slice M6). Ruta literal /instances → NO colisiona con
        // /instances/{id:guid} (la constraint :guid solo casa GUIDs; el listado no lleva segmento).
        // #1 — El tenant lo resuelve el middleware desde el JWT: company-user ve solo su compañía;
        // superadmin ve TODO (tenant null) o acota a una empresa (X-Tenant-Id).
        group.MapGet("/instances", async (
            HttpContext http,
            ListProcedureInstancesHandler handler,
            CancellationToken ct) =>
        {
            var (tenantId, isSuperAdmin) = ResolveTenantContext(http);
            var items = await handler.HandleAsync(tenantId, isSuperAdmin, ct);
            return Results.Ok(new { items });
        }).WithName("ListProcedureInstances");

        // GET /api/v1/tramites/transit-offices — Organismos de tránsito HABILITADOS para la
        // empresa (tenant del header). #2: el operador solo puede elegir/enviar a los OT que la
        // empresa tiene habilitados (admin.tenant_transit_office_grants), resueltos contra el
        // catálogo. Lista vacía si la empresa no tiene ninguno habilitado.
        group.MapGet("/transit-offices", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetTransitGrantsHandler grantsHandler,
            ITransitOfficeCatalog catalog,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var grants = await grantsHandler.HandleAsync(
                new GetTransitGrantsQuery { TenantId = tenantId.Value }, ct);

            var items = grants.TransitOfficeIds
                .Select(catalog.GetById)
                .Where(o => o is not null)
                .Select(o => new TransitOfficeOptionDto(o!.Id, o.Code, o.Name, o.CityCode))
                .ToList();

            return Results.Ok(new { items });
        }).WithName("ListEnabledTransitOffices");

        group.MapGet("/instances/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstance");

        group.MapPatch("/instances/{id:guid}/field-values", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            PatchFieldValuesRequest request,
            PatchFieldValuesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden modificar field_values en estado borrador"),
                // B11 (HU #10659) — en traspaso el OT proviene del RUNT y no puede modificarse.
                "ot_traspaso_no_modificable" => Results.Problem(statusCode: 409, title: "Conflict", detail: "En un traspaso el organismo de tránsito proviene del RUNT y no puede modificarse."),
                "unknown_field" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "field_key no corresponde a ningún campo del tipo de trámite."),
                _ => Results.Ok(result)
            };
        }).WithName("PatchProcedureInstanceFieldValues");

        // R4 (HU #10595) — decisión de prenda (gravamen) del trámite. En matrícula es DECLARATIVA
        // (informativa: no se añade a SubmitGate, por lo que no bloquea la radicación). En traspaso es
        // gate (HU #10597) y admite modificación post-registro versionada (HU #10599). El versionado
        // (nueva vigente reemplaza a la anterior) lo maneja el handler; la prenda vive en su propia
        // tabla, así que escribir fuera de borrador no viola la inmutabilidad de field_values.
        group.MapPut("/instances/{id:guid}/prenda", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RegistrarPrendaInput request,
            HttpContext http,
            RegistrarPrendaHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request, ResolveUserId(http.User), ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "prenda_decision_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "La decisión de prenda no es válida (solicitar|registrar|levantar|omitir|sin_prenda)."),
                // R17 (HU #10599) — un trámite en estado final no admite modificar la prenda.
                TramiteEstadoErrores.EstadoFinal => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.EstadoFinal, detail: "El trámite está en estado final y no admite modificar la prenda."),
                _ => Results.Ok(result)
            };
        }).WithName("PutProcedureInstancePrenda");

        // Lectura de la decisión de prenda vigente del trámite (o null si no hay ninguna).
        group.MapGet("/instances/{id:guid}/prenda", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetPrendaVigenteHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var result = await handler.HandleAsync(id, tenantId.Value, ct);
            return Results.Ok(result);
        }).WithName("GetProcedureInstancePrenda");

        // HU #10349 (AC1) — finalizar borrador: datos completos (actores, docs, organismo) sin exigir
        // identidad ni FUR. Deja la instancia en draft con draft_finalized_at sellado.
        group.MapPost("/instances/{id:guid}/finalize-draft", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            FinalizeDraftProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede finalizar un borrador en estado borrador."),
                "actores_incompletos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Faltan datos de los actores del trámite."),
                "documentos_incompletos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Faltan documentos obligatorios para finalizar el borrador."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de finalizar el borrador."),
                _ => Results.Ok(result)
            };
        }).WithName("FinalizeDraftProcedureInstance");

        // HU #10536 — marcar/desmarcar el trámite como prioritario para que el OT lo revise con
        // primacía. No cambia el estado del ciclo de vida; solo el flag de ordenamiento de los
        // listados. Disponible en cualquier estado (el trámite ya radicado también puede priorizarse
        // para la bandeja del OT).
        group.MapPatch("/instances/{id:guid}/priority", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            SetPriorityRequest request,
            SetPriorityProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request.Prioritario, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("SetProcedureInstancePriority");

        group.MapPost("/instances/{id:guid}/submit", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            SubmitProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            // HU #10431 — la radicación se atribuye al usuario autenticado (claim sub) para alimentar
            // la productividad de la analítica; el handler aplica la guarda FK contra identity.users.
            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ResolveUserId(http.User), ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                // N 03 — el submit radica vía TramiteLifecycleService; códigos del contrato ADR-0022.
                TramiteEstadoErrores.EstadoFinal => Results.Problem(statusCode: 422, title: TramiteEstadoErrores.EstadoFinal, detail: "El trámite está en estado final y no admite radicación."),
                TramiteEstadoErrores.TransicionNoPermitida => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.TransicionNoPermitida, detail: "La instancia ya fue entregada o su estado no permite radicar."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.ConflictoConcurrencia, detail: "El trámite fue modificado por otro proceso. Recargue e intente de nuevo."),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                TramiteEstadoErrores.DocumentosIncompletos => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.DocumentosIncompletos, detail: "Faltan documentos obligatorios para radicar."),
                TramiteEstadoErrores.IdentidadNoAprobada => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.IdentidadNoAprobada, detail: "La validación de identidad no está aprobada o no está vigente."),
                // HU #10459 — gate completo de traspaso: la firma de compraventa bloquea la radicación.
                SubmitGate.FirmaCompraventaRequerida => Results.Problem(statusCode: 409, title: SubmitGate.FirmaCompraventaRequerida, detail: "Falta la firma del contrato de compraventa de comprador y vendedor."),
                "fur_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes de radicar."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de radicar."),
                SubmitGate.ImprontaRequerida => Results.Problem(statusCode: 409, title: SubmitGate.ImprontaRequerida, detail: "Debe generar o cargar la impronta antes de radicar."),
                "organismo_no_habilitado" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El organismo de tránsito seleccionado no está habilitado para la compañía."),
                // HU #10518 — OT con grant pero desactivado/sin tenant a nivel plataforma.
                "organismo_no_operable" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El organismo de tránsito no está operativo en FLIT."),
                "ot_rule_blocked" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El trámite está bloqueado por una regla OT activa."),
                "biometria_requerida_ot" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Se requiere validación biométrica según reglas OT."),
                // R10 (HU #10597) — gate de prenda del traspaso.
                TramiteEstadoErrores.PrendaDecisionRequerida => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.PrendaDecisionRequerida, detail: "El vehículo tiene gravámenes: registra una decisión de prenda antes de radicar."),
                TramiteEstadoErrores.PrendaDocumentoRequerido => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.PrendaDocumentoRequerido, detail: "La decisión de prenda seleccionada requiere adjuntar su documento de soporte."),
                _ => Results.Ok(result)
            };
        }).WithName("SubmitProcedureInstance");

        // N 03 (RF01–RF05) — transición explícita de estado del ciclo de vida. Body: toStatus
        // (borrador|anulado|preparado|entregado|aprobado|rechazado|preasignado|asignado) + reason
        // (obligatorio para anulado/rechazado). preasignado/asignado = ruta de preasignación de placa
        // (Feature #10587). Errores: ProblemDetails con title = código de error (ADR-0022).
        group.MapPost("/instances/{id:guid}/transition", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            TransitionProcedureInstanceRequest request,
            HttpContext http,
            TransitionProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, errorCode, errorDetail) = await handler.HandleAsync(
                id, tenantId.Value, request.ToStatus, request.Reason, ResolveUserId(http.User), ct);

            if (errorCode is null)
                return Results.Ok(result);

            return errorCode switch
            {
                TramiteEstadoErrores.NoEncontrado => Results.Problem(
                    statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(
                    statusCode: 409, title: TramiteEstadoErrores.ConflictoConcurrencia,
                    detail: errorDetail ?? "El trámite fue modificado por otro proceso. Recargue e intente de nuevo."),
                // R10 (HU #10597) — gate de prenda del traspaso (409, subsanable con la decisión/documento).
                TramiteEstadoErrores.PrendaDecisionRequerida or TramiteEstadoErrores.PrendaDocumentoRequerido =>
                    Results.Problem(statusCode: 409, title: errorCode, detail: errorDetail),
                _ => Results.Problem(
                    statusCode: 422, title: errorCode,
                    detail: errorDetail ?? "La transición solicitada no es válida."),
            };
        }).WithName("TransitionProcedureInstance");

        // Feature #10587 (P-10) — placas DISPONIBLES para la compañía en el OT elegido, para el
        // selector del wizard de matrícula inicial. Company-facing: el tenant sale del JWT/header.
        group.MapGet("/plate-preassign/available", async (
            [FromQuery] Guid transitOfficeId,
            HttpContext http,
            IPlateRangeRepository plateRepo,
            CancellationToken ct) =>
        {
            var (resolvedTenant, _) = ResolveTenantContext(http);
            if (resolvedTenant is not { } tenantId || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 403, title: "Forbidden",
                    detail: "El usuario autenticado no tiene una compañía asignada.");

            if (transitOfficeId == Guid.Empty)
                return Results.BadRequest(new { error = "transitOfficeId es obligatorio." });

            var plates = await plateRepo
                .ListDetailsAsync(tenantId, transitOfficeId, PlateState.Disponible, ct)
                .ConfigureAwait(false);
            return Results.Ok(plates);
        }).WithName("ListAvailablePreassignPlates");

        return app;
    }

    /// <summary>
    /// Tenant + rol resueltos por <see cref="TenantEnforcementMiddleware"/> desde el JWT.
    /// <c>TenantId == null</c> solo ocurre para un SuperAdmin sin acotar (ver todo).
    /// </summary>
    private static (Guid? TenantId, bool IsSuperAdmin) ResolveTenantContext(HttpContext http)
    {
        var isSuperAdmin = http.Items.TryGetValue(TenantEnforcementMiddleware.SuperAdminItemKey, out var sa)
            && sa is true;
        Guid? tenantId = http.Items.TryGetValue(TenantEnforcementMiddleware.TenantItemKey, out var t) && t is Guid g
            ? g
            : null;
        return (tenantId, isSuperAdmin);
    }

    /// <summary>Id del usuario autenticado (claim <c>sub</c>/NameIdentifier), o null si no resuelve.</summary>
    private static Guid? ResolveUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>La modalidad solicitada es matrícula inicial (tolerante a espacios/caja).</summary>
    private static bool EsMatriculaInicial(string? modalidad) =>
        string.Equals(
            modalidad?.Trim(),
            TramiteModalidadEntradaCodes.MatriculaInicial,
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Organismo de tránsito habilitado para una empresa (proyección catálogo + grant)
/// que el operador puede elegir en el FUR.
/// </summary>
internal sealed record TransitOfficeOptionDto(Guid Id, string Code, string Name, string CityCode);

/// <summary>Body de POST /instances/{id}/transition (N 03). reason es obligatorio para anulado/rechazado.</summary>
internal sealed record TransitionProcedureInstanceRequest(string? ToStatus, string? Reason);

/// <summary>Body de PATCH /instances/{id}/priority (HU #10536). Prioritario = nuevo valor del flag.</summary>
internal sealed record SetPriorityRequest(bool Prioritario);
