using System.Security.Claims;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.PlatePreassign;
using Flit.Api.Middleware;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Tramites.Enums;
using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Tramites.Domain.Tramites.Services;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Flit.Tramites.Domain.Enums;

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

            // Bloqueo por familia (config compañía → Trámites). Activo = no permitir crear.
            // También se evalúa en CreateProcedureInstanceHandler por procedureType.Family.
            if (EsMatriculaInicial(effectiveRequest.Modalidad))
            {
                var settings = await settingsHandler.HandleAsync(
                    new GetTenantSettingsQuery { TenantId = effectiveRequest.TenantId }, ct);
                var blocked = settings?.SwitchesMatricula.BlockProcedureFamily?.Matriculas
                    ?? settings is not { SwitchesMatricula.AllowInitialRegistration: true };
                if (blocked)
                    return Results.Problem(
                        statusCode: 422,
                        title: "Unprocessable Entity",
                        detail: "La compañía tiene bloqueada la creación de trámites de matrículas. Contacta al administrador.");
            }
            else if (EsTraspaso(effectiveRequest.Modalidad))
            {
                var settings = await settingsHandler.HandleAsync(
                    new GetTenantSettingsQuery { TenantId = effectiveRequest.TenantId }, ct);
                if (settings?.SwitchesMatricula.BlockProcedureFamily?.Traspaso == true)
                    return Results.Problem(
                        statusCode: 422,
                        title: "Unprocessable Entity",
                        detail: "La compañía tiene bloqueada la creación de trámites de traspaso. Contacta al administrador.");
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
                "procedure_family_blocked" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "La compañía tiene bloqueada la creación de trámites de esta familia. Contacta al administrador."),
                // ADR-0050 — el tipo existe y está publicado, pero su recorrido todavía no está
                // habilitado para operarse. Se distingue de `not_published` a propósito: uno es un
                // problema del catálogo y el otro, de la parametrización.
                "procedure_type_not_enabled" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El tipo de trámite todavía no está habilitado para crearse. Contacta al administrador."),
                // FEATURE-08 / HU-BE-02 (CFD-03): validaciones iniciales configurables por gate_profile.
                "COMPANY_RULE_VIOLATION" => Results.Problem(statusCode: 422, title: "COMPANY_RULE_VIOLATION", detail: "El OT del operador no cumple la regla de compañía del tipo."),
                "OT_NOT_AUTHORIZED_FOR_TYPE" => Results.Problem(statusCode: 422, title: "OT_NOT_AUTHORIZED_FOR_TYPE", detail: "El OT del operador no está habilitado/operable para este tipo."),
                "DUPLICATE_ACTIVE_PROCEDURE" => Results.Problem(statusCode: 409, title: "DUPLICATE_ACTIVE_PROCEDURE", detail: "Ya existe un trámite activo del mismo tipo para la placa/VIN."),
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
            ListProcedureInstancesFilteredHandler filteredHandler,
            [FromQuery] string? vin,
            [FromQuery] string? placa,
            [FromQuery] string? vendedor,
            [FromQuery] string? comprador,
            [FromQuery] string? gestor,
            [FromQuery] bool? firmado,
            [FromQuery] DateTimeOffset? createdFrom,
            [FromQuery] DateTimeOffset? createdTo,
            [FromQuery] DateTimeOffset? updatedFrom,
            [FromQuery] DateTimeOffset? updatedTo,
            [FromQuery] string? sortBy,
            [FromQuery] string? sortDir,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            CancellationToken ct) =>
        {
            var (tenantId, _) = ResolveTenantContext(http);

            // Filtrado/ordenamiento server-side (WHERE/ORDER BY en SQL): solo se activa el camino nuevo
            // cuando el caller pide EXPLÍCITAMENTE algún filtro, orden o paginación. Sin ningún parámetro
            // el comportamiento histórico (TOP-N más reciente, sin filtros) queda intacto — no rompe
            // consumidores existentes que llaman este mismo endpoint sin query string.
            var pideFiltradoOrdenado =
                !string.IsNullOrWhiteSpace(vin) || !string.IsNullOrWhiteSpace(placa)
                || !string.IsNullOrWhiteSpace(vendedor) || !string.IsNullOrWhiteSpace(comprador)
                || !string.IsNullOrWhiteSpace(gestor) || firmado is not null
                || createdFrom is not null || createdTo is not null
                || updatedFrom is not null || updatedTo is not null
                || !string.IsNullOrWhiteSpace(sortBy) || !string.IsNullOrWhiteSpace(sortDir)
                || skip is not null || take is not null;

            if (!pideFiltradoOrdenado)
            {
                var items = await handler.HandleAsync(tenantId, ct);
                return Results.Ok(new { items });
            }

            var request = new ProcedureInstanceListRequest
            {
                TenantId = tenantId,
                Skip = skip ?? 0,
                Take = take ?? ListProcedureInstancesHandler.MaxItems,
                Vin = vin,
                Placa = placa,
                Vendedor = vendedor,
                Comprador = comprador,
                Gestor = gestor,
                Firmado = firmado,
                CreatedFrom = createdFrom,
                CreatedTo = createdTo,
                UpdatedFrom = updatedFrom,
                UpdatedTo = updatedTo,
                SortBy = sortBy,
                // Default DESC (igual que el orden histórico); "asc" (case-insensitive) es el único
                // valor que invierte a ascendente — cualquier otro texto se trata como "no asc" (DESC).
                SortDescending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase),
            };

            var (filteredItems, total) = await filteredHandler.HandleAsync(request, ct);
            return Results.Ok(new { items = filteredItems, total });
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

        // HU #11203 — mandatarios que pueden firmar el mandato de este trámite, con su documento y la
        // vigencia de su identidad, más cuál está elegido. Se consulta al registrar, no al aprobar.
        group.MapGet("/instances/{id:guid}/mandate-signers", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            ListMandateSignerOptionsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("ListProcedureInstanceMandateSigners");

        // HU #11203 (AC4/AC5) — fija quién firma. Solo en borrador o subsanación.
        group.MapPut("/instances/{id:guid}/mandate-signer", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            SetMandateSignerBody body,
            SetMandateSignerHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var error = await handler.HandleAsync(id, tenantId.Value, body.MandateSignerId, ct);
            return error switch
            {
                null => Results.NoContent(),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(
                    statusCode: 409,
                    title: "Conflict",
                    detail: "El trámite ya salió de borrador: el mandatario que firma no puede cambiarse."),
                "sin_organismo" => Results.Problem(
                    statusCode: 409,
                    title: "Conflict",
                    detail: "El trámite todavía no tiene organismo de tránsito."),
                _ => Results.Problem(
                    statusCode: 422,
                    title: "Unprocessable Entity",
                    detail: "El mandatario no está habilitado para el organismo de tránsito del trámite."),
            };
        }).WithName("SetProcedureInstanceMandateSigner");

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
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden modificar field_values en borrador o con subsanación activa."),
                // B11 (HU #10659) — en traspaso el OT proviene del RUNT y no puede modificarse.
                "ot_traspaso_no_modificable" => Results.Problem(statusCode: 409, title: "Conflict", detail: "En un traspaso el organismo de tránsito proviene del RUNT y no puede modificarse."),
                // ADR-0050 — la familia OTROS no acumula trámites simultáneos: el cambio ES el trámite.
                PatchFieldValuesHandler.ComplementoNoAdmitidoError => Results.Problem(statusCode: 409, title: PatchFieldValuesHandler.ComplementoNoAdmitidoError, detail: "Este tipo de trámite no admite declarar otra transformación del vehículo: radica un trámite aparte para ese cambio."),
                "unknown_field" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "field_key no corresponde a ningún campo del tipo de trámite."),
                _ => Results.Ok(result)
            };
        }).WithName("PatchProcedureInstanceFieldValues");

        // HU #10975 (Feature #10972) — persiste en field_values los campos que el OCR semántico ya
        // extrae del documento cargado (p. ej. el número de póliza y las fechas del SOAT), que hasta
        // ahora se pintaban en el panel de validación y se descartaban. El endpoint /ocr/{tipo} sigue
        // siendo stateless (no conoce la instancia): es el wizard quien, tras un OCR verificado,
        // manda aquí los campos ya extraídos.
        group.MapPost("/instances/{id:guid}/ocr-fields", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            PersistOcrFieldsRequest request,
            PersistOcrFieldsHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request, ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden escribir field_values en borrador o subsanación."),
                "tipo_no_soportado" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El tipo de documento no tiene campos persistibles por OCR."),
                "invalid_request" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta el tipo de documento."),
                _ => Results.Ok(result)
            };
        }).WithName("PersistProcedureInstanceOcrFields");

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
                // CF-06 (HU #10881) — el organismo exige el certificado: "asumo el riesgo" no es una
                // elección disponible en ese trámite. 409 y no 400: la decisión es válida en general,
                // lo que choca es la regla del OT.
                RegistrarPrendaHandler.OmitirNoAdmitidoError => Results.Problem(statusCode: 409, title: RegistrarPrendaHandler.OmitirNoAdmitidoError, detail: "El organismo de tránsito exige el certificado de prenda: registra o levanta la prenda, o declara que el vehículo no tiene."),
                // ADR-0050 — el tipo no tiene dimensión de gravamen (familia OTROS que no es de prenda).
                RegistrarPrendaHandler.PrendaNoAdmitidaError => Results.Problem(statusCode: 409, title: RegistrarPrendaHandler.PrendaNoAdmitidaError, detail: "Este tipo de trámite no gestiona prenda: para inscribirla o levantarla radica el trámite de prenda correspondiente."),
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

        // HU #10879 — persiste el avance del borrador por pasos (autosave del paso actual del wizard):
        // guarda la Key del paso donde quedó el operador para retomar ahí al reabrir (AC2). Solo en
        // borrador y una vez que la consulta del vehículo está completa (AC1).
        group.MapPatch("/instances/{id:guid}/current-step", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            SetCurrentStepRequest request,
            SetCurrentStepProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, request.Step, ct);
            return error switch
            {
                SetCurrentStepProcedureInstanceHandler.NotFound => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                SetCurrentStepProcedureInstanceHandler.NotDraft => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede persistir el avance en borrador o con subsanación activa."),
                SetCurrentStepProcedureInstanceHandler.VehiculoNoConsultado => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe completar la consulta del vehículo antes de avanzar de paso."),
                SetCurrentStepProcedureInstanceHandler.StepInvalid => Results.Problem(statusCode: 400, title: "Bad Request", detail: "El paso indicado no corresponde a un paso del wizard de este trámite."),
                _ => Results.Ok(result)
            };
        }).WithName("SetProcedureInstanceCurrentStep");

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
                // ICT (servicio v1 pauseDraftProcess) — el trámite está pausado y no avanza hasta reanudarlo.
                TramiteEstadoErrores.TramitePausado => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.TramitePausado, detail: "El trámite está pausado: reanúdelo antes de radicar."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.ConflictoConcurrencia, detail: "El trámite fue modificado por otro proceso. Recargue e intente de nuevo."),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                "procedure_type_not_enabled" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El tipo de trámite todavía no está habilitado para crearse. Contacta al administrador."),
                TramiteEstadoErrores.DocumentosIncompletos => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.DocumentosIncompletos, detail: "Faltan documentos obligatorios para radicar."),
                TramiteEstadoErrores.IdentidadNoAprobada => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.IdentidadNoAprobada, detail: "La validación de identidad no está aprobada o no está vigente."),
                // HU #10459 — gate completo de traspaso: la firma de compraventa bloquea la radicación.
                SubmitGate.FirmaCompraventaRequerida => Results.Problem(statusCode: 409, title: SubmitGate.FirmaCompraventaRequerida, detail: "Falta la firma del contrato de compraventa de comprador y vendedor."),
                "fur_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes de radicar."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de radicar."),
                SubmitGate.ImprontaRequerida => Results.Problem(statusCode: 409, title: SubmitGate.ImprontaRequerida, detail: "Debe generar o cargar la impronta antes de radicar."),
                "organismo_no_habilitado" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El organismo de tránsito seleccionado no está habilitado para la compañía."),
                // HU #10806 — compañía con preasignación activa pero OT mal configurado (grant/allow): se
                // bloquea la radicación para que se corrija la configuración, en vez de degradar a estándar.
                "plate_route_misconfigured" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "La preasignación de placa está activa para tu compañía pero el organismo de tránsito no está habilitado (grant o allow_plate_preassign). Corrige la configuración antes de radicar."),
                // HU #10518 — OT con grant pero desactivado/sin tenant a nivel plataforma.
                "organismo_no_operable" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El organismo de tránsito no está operativo en FLIT."),
                "ot_rule_blocked" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El trámite está bloqueado por una regla OT activa."),
                "biometria_requerida_ot" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Se requiere validación biométrica según reglas OT."),
                // R10 (HU #10597) — gate de prenda del traspaso.
                TramiteEstadoErrores.PrendaDecisionRequerida => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.PrendaDecisionRequerida, detail: "El vehículo tiene gravámenes: registra una decisión de prenda antes de radicar."),
                TramiteEstadoErrores.PrendaDocumentoRequerido => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.PrendaDocumentoRequerido, detail: "La decisión de prenda seleccionada requiere adjuntar su documento de soporte."),
                // CF-06 (HU #10881) — el override compañía+OT, que NO nace de la decisión del gestor.
                TramiteEstadoErrores.PrendaDocumentoRequeridoOt => Results.Problem(statusCode: 409, title: TramiteEstadoErrores.PrendaDocumentoRequeridoOt, detail: "El organismo de tránsito exige adjuntar el documento de prenda para este trámite."),
                // CF-03 (HU #10877) — precondición registral "vehículo ya matriculado" (doble fuente
                // RUNT/FLIT), SEGUNDO momento (el estado pudo cambiar desde el preflight). Bloqueo DURO
                // no subsanable.
                VehicleStatePolicy.ErrorCode => Results.Problem(statusCode: 422, title: VehicleStatePolicy.ErrorCode, detail: "El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite."),
                // Precondición del cambio de carrocería, SEGUNDO momento: cierra la puerta de atrás de
                // un borrador abierto antes de que la guarda del paso 1 existiera.
                VehicleBodyTypePolicy.ErrorCode => Results.Problem(statusCode: 422, title: VehicleBodyTypePolicy.ErrorCode, detail: "El vehículo no tiene carrocería registrada en el RUNT: no es posible radicar un cambio de carrocería."),
                _ => Results.Ok(result)
            };
        }).WithName("SubmitProcedureInstance");

        // ICT (paridad v1 handleChangePausedState) — pausar/reanudar un trámite ICT desde la UI de FLIT.
        // Solo borradores originados por ICT (origin='ict'); un trámite pausado no radica (guard 409 en submit).
        group.MapPut("/instances/{id:guid}/pause", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            PauseProcedureInstanceRequest body,
            HttpContext http,
            PauseProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (ok, error) = await handler.HandleAsync(
                id, tenantId.Value, body.Paused, body.Observation, ResolveUserId(http.User), ct);
            return error switch
            {
                null => Results.Ok(new { id, isPaused = body.Paused, pausedObservation = ok && body.Paused ? body.Observation : null }),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_ict" => Results.Problem(statusCode: 409, title: "not_ict", detail: "Solo se pueden pausar/reanudar trámites originados por ICT."),
                "not_borrador" => Results.Problem(statusCode: 409, title: "not_borrador", detail: "Solo se puede pausar/reanudar un trámite en borrador."),
                _ => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se pudo cambiar el estado de pausa."),
            };
        }).WithName("PauseProcedureInstance");

        // ICT (paridad v1 pause-unpause-massive) — pausar/reanudar en lote. Detalle por trámite.
        group.MapPost("/instances/pause-massive", async (
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            PauseProcedureInstancesBulkRequest body,
            HttpContext http,
            PauseProcedureInstanceHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (body.Ids is null || body.Ids.Count == 0)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Indique al menos un trámite.");

            var results = await handler.HandleBulkAsync(
                body.Ids, tenantId.Value, body.Paused, body.Observation, ResolveUserId(http.User), ct);
            return Results.Ok(new
            {
                total = results.Count,
                processed = results.Count(r => r.Ok),
                detail = results.Select(r => new { id = r.Id, ok = r.Ok, error = r.Error }),
            });
        }).WithName("PauseProcedureInstancesMassive");

        // Sub-flujo placa (HU11037): gestor procesa Asignado → Terminado (checks SOAT/impuesto opcionales).
        group.MapPost("/instances/{id:guid}/plate-flow/complete", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            CompletePlateFlowRequest? body,
            CompletePlateFlowHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error, warning) = await handler.HandleAsync(
                id, tenantId.Value, ResolveUserId(http.User), body ?? new CompletePlateFlowRequest(), ct);
            return error switch
            {
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                TramiteEstadoErrores.TransicionNoPermitida => Results.Problem(
                    statusCode: 409, title: TramiteEstadoErrores.TransicionNoPermitida,
                    detail: "El trámite no está en entregado o no admite completar el flujo de placa."),
                "plate_flow_not_asignado" => Results.Problem(
                    statusCode: 409, title: "plate_flow_not_asignado",
                    detail: "Solo se puede procesar cuando el sub-estado de placa es asignado."),
                CompletePlateFlowHandler.SoatNoVigente => Results.Problem(
                    statusCode: 409, title: CompletePlateFlowHandler.SoatNoVigente,
                    detail: "El RUNT no reporta un SOAT vigente para el vehículo. La compañía tiene "
                        + "desactivada la opción de continuar sin SOAT vigente: registra un SOAT vigente y vuelve a intentarlo."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(
                    statusCode: 409, title: TramiteEstadoErrores.ConflictoConcurrencia,
                    detail: "El trámite cambió mientras se procesaba. Recarga e inténtalo de nuevo."),
                // 200 con advertencia: el trámite avanzó, pero el gestor tiene que saber con qué salvedad.
                _ => Results.Ok(new CompletePlateFlowResponse(
                    result,
                    warning,
                    warning == CompletePlateFlowHandler.SoatNoVigenteAdvertencia
                        ? "El trámite se envió al OT SIN SOAT vigente: el RUNT no lo reporta vigente. "
                            + "La compañía permite continuar, pero el OT puede rechazarlo por este motivo."
                        : null))
            };
        }).WithName("CompletePlateFlow");

        // Activa subsanación sobre rechazado (flag, sin cambiar status). Solo permitido en rechazado.
        group.MapPost("/instances/{id:guid}/subsanar", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            StartSubsanacionHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ResolveUserId(http.User), ct: ct);
            return error switch
            {
                null => Results.Ok(result),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_rechazado" => Results.Problem(
                    statusCode: 409, title: "Conflict",
                    detail: "Solo un trámite en estado rechazado puede iniciar subsanación."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(
                    statusCode: 409, title: TramiteEstadoErrores.ConflictoConcurrencia,
                    detail: "El trámite fue modificado por otro proceso. Recargue e intente de nuevo."),
                _ => Results.Problem(statusCode: 422, title: error, detail: "No se pudo iniciar la subsanación."),
            };
        }).WithName("StartSubsanacionProcedureInstance");

        // Cancela la subsanación (apaga el flag) sobre rechazado. El status sigue en rechazado.
        group.MapPost("/instances/{id:guid}/cancelar-subsanacion", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            HttpContext http,
            CancelSubsanacionHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ResolveUserId(http.User), ct);
            return error switch
            {
                null => Results.Ok(result),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_rechazado" => Results.Problem(
                    statusCode: 409, title: "Conflict",
                    detail: "Solo un trámite en estado rechazado puede cancelar la subsanación."),
                TramiteEstadoErrores.ConflictoConcurrencia => Results.Problem(
                    statusCode: 409, title: TramiteEstadoErrores.ConflictoConcurrencia,
                    detail: "El trámite fue modificado por otro proceso. Recargue e intente de nuevo."),
                _ => Results.Problem(statusCode: 422, title: error, detail: "No se pudo cancelar la subsanación."),
            };
        }).WithName("CancelSubsanacionProcedureInstance");

        // N 03 (RF01–RF05) — transición explícita de estado del ciclo de vida. Body: toStatus
        // (borrador|anulado|preparado|entregado|aprobado|rechazado)
        // + reason (obligatorio para anulado/rechazado). La subsanación ya no es un estado: se activa
        // con POST /subsanar sobre rechazado; el re-radicado es rechazado→entregado vía submit.
        // Errores: ProblemDetails con title = código de error (ADR-0022).
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
                id, tenantId.Value, request.ToStatus, request.Reason, ResolveUserId(http.User),
                request.MandateSignerId, ct);

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
                // CF-06 (HU #10881) — y el override compañía+OT, con código propio desde 2026-08-12 para que
                // el mensaje pueda decir que el origen es una regla del organismo, no la decisión del gestor.
                TramiteEstadoErrores.PrendaDecisionRequerida
                    or TramiteEstadoErrores.PrendaDocumentoRequerido
                    or TramiteEstadoErrores.PrendaDocumentoRequeridoOt =>
                    Results.Problem(statusCode: 409, title: errorCode, detail: errorDetail),
                // ADR-0036 §D9 (HU #10916) — al aprobar hay varios mandatarios y ninguno cotejó: elegir uno
                // (409, subsanable reintentando con mandateSignerId).
                TramiteEstadoErrores.MandatarioRequerido =>
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

        // HU #10806 (AC3) — ¿la ruta de preasignación de placa está ACTIVA para la compañía del
        // radicador en el OT elegido? El wizard lo consulta para no mostrar el selector como si
        // preasignara cuando en realidad el trámite se entregará de forma estándar. Reutiliza el
        // mismo AND de tres flags que el submit (IsAssignmentAllowedAsync).
        group.MapGet("/plate-preassign/status", async (
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

            var enabled = await plateRepo
                .IsAssignmentAllowedAsync(tenantId, transitOfficeId, ct)
                .ConfigureAwait(false);
            return Results.Ok(new { enabled });
        }).WithName("PlatePreassignStatus");

        // CF-02 (HU #10879 AC3 / #10883 AC3) — consulta del vehículo del PASO 1 SIN crear el trámite.
        // Devuelve el mismo semáforo y los mismos bloqueos (409 duplicidad / 422 estado registral) que
        // el preflight de una instancia, pero no persiste nada: si el operador abandona aquí, no queda
        // ningún registro. El token devuelto se entrega luego a /instances/from-consulta.
        group.MapPost("/preflight-preview", async (
            PreflightPreviewBody body,
            HttpContext http,
            RunPreflightPreviewHandler handler,
            GetTenantSettingsHandler settingsHandler,
            CancellationToken ct) =>
        {
            var (tenant, error) = await ResolveEffectiveTenantAsync(http, body.TenantId, body.Modalidad, settingsHandler, ct);
            if (error is not null)
                return error;

            var (result, err, existingId, vehicleState) = await handler.HandleAsync(
                new PreflightPreviewRequest(
                    tenant,
                    body.Modalidad,
                    body.Vin,
                    body.Plate,
                    body.OwnerDocumentType,
                    body.OwnerDocumentNumber,
                    body.TransitOfficeId,
                    body.ProcedureTypeCode),
                ct);

            return err switch
            {
                "modalidad_not_available" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay un tipo de trámite publicado para la modalidad indicada."),
                "procedure_type_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "El tipo de trámite indicado no existe o no está publicado."),
                "identificador_requerido" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Indique el identificador del vehículo (VIN o placa, según el tipo de trámite) para consultar."),
                // HU #11199 (AC2) — en matrícula inicial la consulta por VIN no corre sin secretaría.
                TransitOfficeSelectionPolicy.RequiredErrorCode => Results.Problem(
                    statusCode: 400,
                    title: TransitOfficeSelectionPolicy.RequiredErrorCode,
                    detail: "Seleccione la secretaría de tránsito antes de consultar el vehículo."),
                // HU #11199 (AC3) / HU #11200 (AC2/AC3) — el organismo no está activo en FLIT o no está
                // habilitado para la compañía gestora.
                TransitOfficeSelectionPolicy.UnavailableErrorCode => Results.Problem(
                    statusCode: 422,
                    title: TransitOfficeSelectionPolicy.UnavailableErrorCode,
                    detail: "El organismo de tránsito no está activo en FLIT o no está habilitado para la compañía."),
                InitialProcedureValidationGate.DuplicateActiveProcedure => Results.Problem(
                    statusCode: 409,
                    title: InitialProcedureValidationGate.DuplicateActiveProcedure,
                    detail: "Ya existe un trámite en proceso para este VIN/placa.",
                    extensions: new Dictionary<string, object?> { ["procedureInstanceId"] = existingId }),
                VehicleStatePolicy.ErrorCode => Results.Problem(
                    statusCode: 422,
                    title: VehicleStatePolicy.ErrorCode,
                    detail: "El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["vehicleStatus"] = vehicleState?.VehicleStatus,
                        ["procedureType"] = vehicleState?.ProcedureType,
                    }),
                // El vehículo no tiene carrocería que cambiar. Se avisa aquí —con el trámite todavía
                // sin crear— para que el gestor pueda escoger otro tipo sin arrastrar un expediente.
                VehicleBodyTypePolicy.ErrorCode => Results.Problem(
                    statusCode: 422,
                    title: VehicleBodyTypePolicy.ErrorCode,
                    detail: "El vehículo no tiene carrocería registrada en el RUNT: no es posible radicar un cambio de carrocería.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["procedureType"] = VehicleBodyTypePolicy.ProcedureTypeCambioCarroceria,
                    }),
                // El vehículo no tiene prenda que levantar. Igual que el anterior: se avisa con el
                // trámite todavía sin crear, para que el gestor pueda escoger otro tipo.
                VehiclePrendaPolicy.ErrorCode => Results.Problem(
                    statusCode: 422,
                    title: VehiclePrendaPolicy.ErrorCode,
                    detail: "El vehículo no tiene prenda registrada en el RUNT: no hay gravamen que levantar.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["procedureType"] = VehiclePrendaPolicy.ProcedureTypeLevantamiento,
                    }),
                _ => Results.Ok(result),
            };
        }).WithName("RunProcedureInstancePreflightPreview");

        // HU sin ADO 2026-08-11 — consulta RUES por NIT SIN trámite creado (paso 1, casilla 19 del FUR:
        // "EMPRESA VINCULADORA"). Hermano de /preflight-preview: no lleva instancia en la ruta y NO
        // persiste nada (ver XML doc de RuesPreviewHandler / ADR-0041 — procedure_instance_id NOT NULL
        // es imposible de cumplir aquí). "El proveedor no encontró el NIT" (200, found:false) y "el
        // proveedor no respondió" (503) son casos DISTINTOS a propósito: el frontend los trata distinto
        // (found:false → cae al ingreso manual; 503 → reintentar o avisar que el servicio no está
        // disponible).
        group.MapPost("/rues-preview", async (
            RuesPreviewBody body,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            RuesPreviewHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(body.DocumentNumber, tenantId.Value, ct);

            return error switch
            {
                "invalid_request" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Se requiere documentNumber (NIT)."),
                "provider_not_found" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "El proveedor RUES no está disponible."),
                // El proveedor está registrado pero la consulta falló (no-200, timeout, red, respuesta
                // ilegible). Se responde 503 igual que si no estuviera configurado: para el operador
                // ambos son "reintenta en unos minutos", y lo que NO puede pasar es que se le diga que
                // su NIT no existe cuando el problema está del lado del servicio.
                "provider_unavailable" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "No fue posible consultar el RUES en este momento. Reintenta en unos minutos."),
                _ => Results.Ok(result),
            };
        }).WithName("RuesPreview");

        // CF-02 (HU #10879 AC5 / #10883 AC4) — creación del trámite AL AVANZAR al segundo paso, con el
        // vehículo ya consultado. Reemplaza al POST /instances "vacío" en el flujo del wizard: crea,
        // persiste los identificadores capturados y deja el preflight persistido en una sola operación,
        // reusando la consulta del paso 1 (sin segunda llamada al proveedor externo).
        group.MapPost("/instances/from-consulta", async (
            CreateFromConsultaBody body,
            HttpContext http,
            CreateProcedureInstanceFromConsultaHandler handler,
            GetTenantSettingsHandler settingsHandler,
            CancellationToken ct) =>
        {
            var (tenant, error) = await ResolveEffectiveTenantAsync(http, body.TenantId, body.Modalidad, settingsHandler, ct);
            if (error is not null)
                return error;

            var (result, err, existingId, vehicleState) = await handler.HandleAsync(
                new CreateFromConsultaRequest(
                    tenant,
                    ResolveUserId(http.User) ?? body.CreatedByUserId,
                    body.Modalidad,
                    body.Vin,
                    body.Plate,
                    body.OwnerDocumentType,
                    body.OwnerDocumentNumber,
                    body.PreviewToken,
                    body.TransitOfficeId,
                    body.TipoServicioCode,
                    body.EmpresaVinculadoraNit,
                    body.EmpresaVinculadoraRazonSocial,
                    body.ProcedureTypeCode),
                ct);

            return err switch
            {
                "identificador_requerido" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "Indique el identificador del vehículo (VIN o placa, según el tipo de trámite) para crear el trámite."),
                // ADR-0050 — el tipo elegido no existe o no está publicado. Se distingue de
                // `modalidad_not_available`: aquí el catálogo SÍ se consultó y el code no está.
                "procedure_type_not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "El tipo de trámite indicado no existe o no está publicado."),
                // HU sin ADO 2026-08-11 — tipoServicioCode debe ser uno de los 6 códigos cerrados
                // (VehicleServiceTypeCode). Es entrada estructurada del selector, no texto libre del
                // RUNT: un valor fuera del catálogo se rechaza en vez de caer en silencio a "Particular".
                "invalid_tipo_servicio" => Results.Problem(
                    statusCode: 400,
                    title: "Bad Request",
                    detail: "tipoServicioCode inválido: debe ser uno de PARTICULAR, PUBLICO, DIPLOMATICO, OFICIAL, ESPECIAL, OTROS."),
                // HU #11199 (AC1/AC3) — la secretaría del paso 1 se re-confirma al crear el trámite.
                TransitOfficeSelectionPolicy.RequiredErrorCode => Results.Problem(
                    statusCode: 400,
                    title: TransitOfficeSelectionPolicy.RequiredErrorCode,
                    detail: "Seleccione la secretaría de tránsito antes de continuar."),
                TransitOfficeSelectionPolicy.UnavailableErrorCode => Results.Problem(
                    statusCode: 422,
                    title: TransitOfficeSelectionPolicy.UnavailableErrorCode,
                    detail: "El organismo de tránsito no está activo en FLIT o no está habilitado para la compañía."),
                "modalidad_not_available" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay un tipo de trámite publicado para la modalidad indicada."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure type not found."),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                "procedure_type_not_enabled" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El tipo de trámite todavía no está habilitado para crearse. Contacta al administrador."),
                "invalid_reference" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El tenant, el usuario o el tipo de trámite indicado no existe."),
                "reference_conflict" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se pudo generar un número de referencia único. Reintente."),
                "COMPANY_RULE_VIOLATION" => Results.Problem(statusCode: 422, title: "COMPANY_RULE_VIOLATION", detail: "El OT del operador no cumple la regla de compañía del tipo."),
                "OT_NOT_AUTHORIZED_FOR_TYPE" => Results.Problem(statusCode: 422, title: "OT_NOT_AUTHORIZED_FOR_TYPE", detail: "El OT del operador no está habilitado/operable para este tipo."),
                InitialProcedureValidationGate.DuplicateActiveProcedure => Results.Problem(
                    statusCode: 409,
                    title: InitialProcedureValidationGate.DuplicateActiveProcedure,
                    detail: "Ya existe un trámite en proceso para este VIN/placa.",
                    extensions: new Dictionary<string, object?> { ["procedureInstanceId"] = existingId }),
                VehicleStatePolicy.ErrorCode => Results.Problem(
                    statusCode: 422,
                    title: VehicleStatePolicy.ErrorCode,
                    detail: "El vehículo ya se encuentra matriculado: no es válido para este tipo de trámite.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["vehicleStatus"] = vehicleState?.VehicleStatus,
                        ["procedureType"] = vehicleState?.ProcedureType,
                    }),
                null => Results.Created($"/api/v1/tramites/instances/{result!.Instance.Id}", result),
                _ => Results.Problem(statusCode: 409, title: "Conflict", detail: "No se pudo crear el trámite."),
            };
        }).WithName("CreateProcedureInstanceFromConsulta");

        return app;
    }

    /// <summary>
    /// Tenant efectivo + gate de matrícula inicial, compartidos por el preview del paso 1 y por la
    /// creación al avanzar al paso 2: ambos deben respetar exactamente las mismas reglas que el POST
    /// /instances original (#1 el tenant sale del JWT; #5 la compañía debe habilitar la modalidad).
    /// Devuelve el error listo para retornar cuando alguna regla falla.
    /// </summary>
    private static async Task<(Guid Tenant, IResult? Error)> ResolveEffectiveTenantAsync(
        HttpContext http,
        Guid bodyTenantId,
        /// <summary>Familia del trámite a crear (o la modalidad heredada, que se traduce).</summary>
        string? familyCode,
        GetTenantSettingsHandler settingsHandler,
        CancellationToken ct)
    {
        var (resolvedTenant, isSuperAdmin) = ResolveTenantContext(http);
        Guid effectiveTenant;
        if (isSuperAdmin)
        {
            effectiveTenant = resolvedTenant ?? bodyTenantId;
            if (effectiveTenant == Guid.Empty)
            {
                return (Guid.Empty, Results.Problem(statusCode: 400, title: "Bad Request",
                    detail: "Indique la compañía destino (X-Tenant-Id) para crear el trámite."));
            }
        }
        else if (resolvedTenant is { } companyTenant)
        {
            effectiveTenant = companyTenant;
        }
        else
        {
            return (Guid.Empty, Results.Problem(statusCode: 403, title: "Forbidden",
                detail: "El usuario autenticado no tiene una compañía asignada."));
        }

        // ADR-0050 — el bloqueo por compañía cubre las TRES familias. Antes solo miraba matrículas y
        // traspaso: un trámite de la familia OTROS pasaba el gate sin que nadie lo evaluara, así que
        // el interruptor `otros` de la configuración de la compañía no bloqueaba nada.
        var familia = ProcedureFamilyCodes.FromCodeOrLegacyModalidad(familyCode);
        if (familia is null)
            return (effectiveTenant, null);

        var settings = await settingsHandler.HandleAsync(
            new GetTenantSettingsQuery { TenantId = effectiveTenant }, ct);
        var bloqueo = settings?.SwitchesMatricula.BlockProcedureFamily;

        var (bloqueada, etiqueta) = familia switch
        {
            ProcedureFamily.Matriculas => (
                // La matrícula conserva su interruptor histórico `AllowInitialRegistration` como
                // respaldo: la compañía sin ajustes cargados no puede crearlas.
                bloqueo?.Matriculas ?? settings is not { SwitchesMatricula.AllowInitialRegistration: true },
                "matrículas"),
            ProcedureFamily.Traspaso => (bloqueo?.Traspaso == true, "traspaso"),
            _ => (bloqueo?.Otros == true, "otros trámites"),
        };

        if (bloqueada)
        {
            return (effectiveTenant, Results.Problem(
                statusCode: 422,
                title: "Unprocessable Entity",
                detail: $"La compañía tiene bloqueada la creación de trámites de {etiqueta}. Contacta al administrador."));
        }

        return (effectiveTenant, null);
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
            ProcedureFamilyCodes.Matriculas,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>La modalidad solicitada es traspaso (tolerante a espacios/caja).</summary>
    private static bool EsTraspaso(string? modalidad) =>
        string.Equals(
            modalidad?.Trim(),
            ProcedureFamilyCodes.Traspaso,
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Organismo de tránsito habilitado para una empresa (proyección catálogo + grant)
/// que el operador puede elegir en el FUR.
/// </summary>
internal sealed record TransitOfficeOptionDto(Guid Id, string Code, string Name, string CityCode);

/// <summary>
/// Body de POST /instances/{id}/transition (N 03). reason es obligatorio para anulado/rechazado.
/// mandateSignerId (ADR-0036 §D9, HU #10916) elige el mandatario al aprobar cuando hay varios sin cotejo
/// (subsana el 409 mandatario_requerido); se ignora en el resto de transiciones.
/// </summary>
internal sealed record TransitionProcedureInstanceRequest(string? ToStatus, string? Reason, Guid? MandateSignerId = null);

/// <summary>Body de PATCH /instances/{id}/priority (HU #10536). Prioritario = nuevo valor del flag.</summary>
internal sealed record SetPriorityRequest(bool Prioritario);

/// <summary>
/// Body de PUT /instances/{id}/pause (paridad v1). <c>Paused</c> = nuevo estado (true=pausar,
/// false=reanudar); <c>Observation</c> = nota informativa (se guarda solo al pausar; se limpia al reanudar).
/// </summary>
internal sealed record PauseProcedureInstanceRequest(bool Paused, string? Observation = null);

/// <summary>Body de POST /instances/pause-massive (paridad v1 pause-unpause-massive).</summary>
internal sealed record PauseProcedureInstancesBulkRequest(
    IReadOnlyList<Guid> Ids, bool Paused, string? Observation = null);

/// <summary>Body de PATCH /instances/{id}/current-step (HU #10879). Step = Key del paso del wizard.</summary>
internal sealed record SetCurrentStepRequest(string? Step);

/// <summary>
/// Respuesta de POST /instances/{id}/plate-flow/complete. El trámite avanzó (<c>Instance</c>), pero
/// puede traer una salvedad que el gestor debe ver: <c>WarningCode</c> para lógica y
/// <c>WarningMessage</c> ya redactado para la UI. Ambos van en null cuando no hay nada que advertir.
/// </summary>
internal sealed record CompletePlateFlowResponse(
    ProcedureInstanceSummary? Instance,
    string? WarningCode,
    string? WarningMessage);

/// <summary>
/// Body de POST /preflight-preview (CF-02). <c>TenantId</c> solo lo usa el SuperAdmin sin
/// <c>X-Tenant-Id</c>; para un usuario de compañía el backend lo impone desde el JWT.
/// </summary>
/// <summary>HU #11203 — cuerpo de la elección del mandatario que firma el mandato del trámite.</summary>
internal sealed record SetMandateSignerBody(Guid MandateSignerId);

internal sealed record PreflightPreviewBody(
    Guid TenantId,
    string Modalidad,
    string? Vin,
    string? Plate,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber,
    /// <summary>HU #11199 — secretaría del paso 1; obligatoria en matrícula inicial.</summary>
    Guid? TransitOfficeId,
    /// <summary>ADR-0050 — `code` del tipo elegido; decide qué identificador exige la consulta.</summary>
    string? ProcedureTypeCode = null);

/// <summary>Body de POST /rues-preview (HU sin ADO 2026-08-11). NIT a consultar en RUES.</summary>
internal sealed record RuesPreviewBody(string? DocumentNumber);

/// <summary>
/// Body de POST /instances/from-consulta (CF-02). <c>PreviewToken</c> es el de la consulta del paso 1:
/// si falta o expiró, el preflight vuelve a consultar (degradación, no error).
/// </summary>
internal sealed record CreateFromConsultaBody(
    Guid TenantId,
    Guid CreatedByUserId,
    string Modalidad,
    string? Vin,
    string? Plate,
    string? OwnerDocumentType,
    string? OwnerDocumentNumber,
    string? PreviewToken,
    Guid? TransitOfficeId,
    /// <summary>
    /// HU sin ADO 2026-08-11 — casilla 18 del FUR (tipo de servicio), elegido por el operador en
    /// MATRÍCULA INICIAL. Uno de los 6 códigos cerrados de <see cref="VehicleServiceTypeCode"/>. Se
    /// ignora fuera de matrícula inicial (mismo criterio que <see cref="TransitOfficeId"/>).
    /// </summary>
    string? TipoServicioCode = null,
    /// <summary>
    /// HU sin ADO 2026-08-11 — casilla 19 del FUR (empresa vinculadora). Solo tiene efecto cuando
    /// <see cref="TipoServicioCode"/> es <c>PUBLICO</c>; con cualquier otro valor (o ausente) se
    /// ignora, ver <c>CreateProcedureInstanceFromConsultaHandler</c>.
    /// </summary>
    /// <summary>
    /// ADR-0050 — <c>code</c> del tipo elegido en el catálogo. Cuando viene MANDA sobre
    /// <see cref="Modalidad"/>, que queda solo como familia para el bloqueo por compañía.
    /// </summary>
    string? ProcedureTypeCode = null,
    string? EmpresaVinculadoraNit = null,
    string? EmpresaVinculadoraRazonSocial = null);
