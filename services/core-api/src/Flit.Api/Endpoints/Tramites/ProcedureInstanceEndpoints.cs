using System.Security.Claims;
using Flit.Admin.Application.Companies.Settings.GetTenantSettings;
using Flit.Admin.Application.Companies.TransitOffices.GetTransitGrants;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Api.Middleware;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Enums;
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

            // #5 — La compañía debe tener HABILITADA explícitamente la matrícula inicial vía
            // el toggle "Permitir matrícula inicial" (admin/companies) para poder crear ese
            // trámite. Solo se permite cuando existe fila de settings Y el flag está en true:
            // sin fila (tenant aún no configurado) o con el flag en false → se bloquea. Así se
            // alinea con el default del admin (toggle apagado para empresas nuevas) y se evita
            // iniciar matrícula inicial en compañías sin configuración.
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
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se pueden modificar field_values en estado draft"),
                "unknown_field" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "field_key no corresponde a ningún campo del tipo de trámite."),
                _ => Results.Ok(result)
            };
        }).WithName("PatchProcedureInstanceFieldValues");

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
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede finalizar un borrador en estado draft."),
                "actores_incompletos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Faltan datos de los actores del trámite."),
                "documentos_incompletos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Faltan documentos obligatorios para finalizar el borrador."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de finalizar el borrador."),
                _ => Results.Ok(result)
            };
        }).WithName("FinalizeDraftProcedureInstance");

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
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La instancia ya fue enviada o no está en draft"),
                "not_published" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El tipo de trámite no está publicado."),
                "documentos_incompletos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Faltan documentos obligatorios para radicar."),
                "identidad_requerida" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La validación de identidad del comprador no está aprobada."),
                "fur_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes de radicar."),
                "organismo_requerido" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe seleccionar el organismo de tránsito antes de radicar."),
                "organismo_no_habilitado" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El organismo de tránsito seleccionado no está habilitado para la compañía."),
                "ot_rule_blocked" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El trámite está bloqueado por una regla OT activa."),
                "biometria_requerida_ot" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Se requiere validación biométrica según reglas OT."),
                _ => Results.Ok(result)
            };
        }).WithName("SubmitProcedureInstance");

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
