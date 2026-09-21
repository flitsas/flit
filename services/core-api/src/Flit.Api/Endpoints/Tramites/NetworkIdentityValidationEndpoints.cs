using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12708 (Feature #12705, Épica #12685) — Validación de Identidad de la red: la cabeza de grupo
/// (Concesión / Marca Blanca) consulta en SOLO LECTURA las validaciones y prevalidaciones de su compañía
/// y de sus hijas.
/// <list type="bullet">
///   <item><c>GET /identity-validations/by-person</c>: grilla por persona consolidada (o de una compañía
///   con <c>childTenantId</c>), paginada con el tope de la vista propia, con la compañía de cada fila.</item>
///   <item><c>GET /identity-validations/by-person/detail</c>: historial de una persona de UNA compañía
///   (<c>childTenantId</c> obligatorio: la misma cédula puede estar en dos hijas).</item>
///   <item><c>GET /identity-validations/{validationId}/audit</c>: bitácora; fuera del alcance ⇒ 404 igual
///   que un id inexistente.</item>
/// </list>
/// Vive en el grupo <c>/api/v1/tramites/network</c> (<see cref="NetworkProcedureEndpoints"/>): hereda
/// <see cref="GroupHeadReadFilter"/> (403 <c>network_scope_required</c> / <c>network_role_required</c> sin
/// consultar) y <see cref="NetworkAccessAuditFilter"/> (una fila por acceso a datos de una hija). Sin
/// interruptor propio: con <c>group_read_scope</c> apagado toda cabeza degrada a <c>Single</c> y la policy
/// la rechaza, como al resto de la red. No hay rutas de escritura: editar, reenviar o reencolar sigue
/// siendo de la compañía dueña por sus rutas propias. La cabeza ve el correo enmascarado y nunca el enlace
/// de captura (<see cref="NetworkIdentityRedaction"/>).
/// </summary>
internal static class NetworkIdentityValidationEndpoints
{
    private static readonly JsonSerializerOptions FiltersJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>El número de documento del historial se audita solo por nombre, nunca su valor.</summary>
    private static readonly string[] DocumentNumberOnly = ["documentNumber"];

    internal static RouteGroupBuilder MapNetworkIdentityValidations(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/identity-validations/by-person", async (
            HttpContext http,
            NetworkListIdentityPersonsHandler handler,
            [FromQuery] Guid? childTenantId,
            [FromQuery] string? name,
            [FromQuery] string? documentType,
            [FromQuery] string? documentNumber,
            [FromQuery] string? status,
            [FromQuery] DateTimeOffset? createdFrom,
            [FromQuery] DateTimeOffset? createdTo,
            [FromQuery] string? vigenciaEstado,
            [FromQuery] DateTimeOffset? expiraDesde,
            [FromQuery] DateTimeOffset? expiraHasta,
            [FromQuery] int? venceEnDias,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] bool? standalone,
            CancellationToken ct) =>
        {
            var query = new TenantBiometricPersonListQuery(
                name, documentType, documentNumber, status, createdFrom, createdTo, vigenciaEstado,
                expiraDesde, expiraHasta, venceEnDias,
                page ?? 1,
                pageSize ?? TenantBiometricValidationListQuery.DefaultPageSize,
                standalone);

            var (result, error) = await handler.HandleAsync(
                RequestTenantResolver.ScopeFromItems(http), childTenantId, query, ct);

            var filters = FiltersJson(childTenantId, query);
            if (result is not null)
            {
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    NetworkAccessVocabulary.Resources.IdentityValidationsSearch,
                    NetworkAccessVocabulary.Results.Ok,
                    result.Persons.Select(p => p.TenantId).Distinct().ToArray(),
                    filters));
            }
            else if (error == NetworkScopePolicy.ChildOutOfScope && childTenantId is { } child)
            {
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    NetworkAccessVocabulary.Resources.IdentityValidationsSearch,
                    NetworkAccessVocabulary.Results.Forbidden,
                    [child],
                    filters));
            }

            return error switch
            {
                null => Results.Ok(result),
                NetworkScopePolicy.ScopeRequired or NetworkScopePolicy.ChildOutOfScope or NetworkScopePolicy.RoleRequired
                    => Forbidden(error),
                _ => Results.Problem(statusCode: 400, title: "Bad Request", detail: error),
            };
        })
            .WithName("NetworkListIdentityPersons")
            // HU #12711 — mismo permiso del módulo que las rutas propias: sin él, tampoco la red.
            .RequireIdentityModuleRead()
            .WithSummary("Validaciones de identidad de la red por persona (cabeza de grupo, solo lectura)")
            .Produces<NetworkIdentityPersonsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/identity-validations/by-person/detail", async (
            HttpContext http,
            NetworkListPersonIdentityValidationsHandler handler,
            [FromQuery] Guid? childTenantId,
            [FromQuery] string? documentType,
            [FromQuery] string? documentNumber,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(
                RequestTenantResolver.ScopeFromItems(http),
                childTenantId,
                documentType,
                documentNumber,
                page ?? 1,
                pageSize ?? ListPersonBiometricValidationsHandler.DefaultPageSize,
                ct);

            // El acceso a la persona de una hija se audita con su desenlace; la cabeza sobre sí misma
            // no deja rastro (el filtro de auditoría descarta a la propia cabeza).
            var auditResult = error switch
            {
                null => NetworkAccessVocabulary.Results.Ok,
                "not_found" => NetworkAccessVocabulary.Results.NotFound,
                NetworkScopePolicy.ChildOutOfScope => NetworkAccessVocabulary.Results.Forbidden,
                _ => null,
            };
            if (auditResult is not null && childTenantId is { } child)
            {
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    NetworkAccessVocabulary.Resources.IdentityValidationsDetail,
                    auditResult,
                    [child],
                    JsonSerializer.Serialize(new { childTenantId, documentType, textFilters = DocumentNumberOnly }, FiltersJsonOptions)));
            }

            return error switch
            {
                null => Results.Ok(result),
                NetworkScopePolicy.ScopeRequired or NetworkScopePolicy.ChildOutOfScope or NetworkScopePolicy.RoleRequired
                    => Forbidden(error),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found",
                    detail: "No hay validaciones de identidad para ese documento."),
                NetworkIdentityErrors.ChildRequired => Results.Problem(statusCode: 400, title: "Bad Request",
                    detail: "childTenantId es obligatorio: indica la compañía de la persona."),
                "documento_requerido" => Results.Problem(statusCode: 400, title: "Bad Request",
                    detail: "documentType y documentNumber son obligatorios."),
                _ => Results.Problem(statusCode: 400, title: "Bad Request", detail: error),
            };
        })
            .WithName("NetworkListPersonIdentityValidations")
            // HU #12711 — mismo permiso del módulo que las rutas propias: sin él, tampoco la red.
            .RequireIdentityModuleRead()
            .WithSummary("Historial de identidad de una persona de una compañía de la red (solo lectura)")
            .Produces<PersonBiometricValidationsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/identity-validations/{validationId:guid}/audit", async (
            Guid validationId,
            HttpContext http,
            NetworkGetIdentityAuditHandler handler,
            CancellationToken ct) =>
        {
            var (result, owner, error) = await handler.HandleAsync(
                RequestTenantResolver.ScopeFromItems(http), validationId, ct);

            // Un 404 no publica nada: no se sabe (ni se revela) de quién sería la validación.
            if (result is not null && owner is { } ownerTenant)
            {
                NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
                    NetworkAccessVocabulary.Resources.IdentityValidationsAudit,
                    NetworkAccessVocabulary.Results.Ok,
                    [ownerTenant]));
            }

            return error switch
            {
                null => Results.Ok(result),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found",
                    detail: "Validación de identidad no encontrada."),
                _ => Forbidden(error),
            };
        })
            .WithName("NetworkGetIdentityAudit")
            // HU #12711 — mismo permiso del módulo que las rutas propias: sin él, tampoco la red.
            .RequireIdentityModuleRead()
            .WithSummary("Bitácora de una validación de identidad de la red (solo lectura)")
            .Produces<IdentityAuditResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>
    /// Filtros de la grilla para la auditoría (AC8): valores de catálogo y fechas tal cual; nombre y número
    /// de documento solo por NOMBRE de filtro (<c>textFilters</c>), nunca su contenido.
    /// </summary>
    internal static string FiltersJson(Guid? childTenantId, TenantBiometricPersonListQuery query)
    {
        var textFilters = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(query.Name)) textFilters.Add("name");
        if (!string.IsNullOrWhiteSpace(query.DocumentNumber)) textFilters.Add("documentNumber");

        return JsonSerializer.Serialize(new
        {
            childTenantId,
            documentType = string.IsNullOrWhiteSpace(query.DocumentType) ? null : query.DocumentType,
            status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status,
            createdFrom = query.CreatedFrom,
            createdTo = query.CreatedTo,
            vigenciaEstado = string.IsNullOrWhiteSpace(query.VigenciaEstado) ? null : query.VigenciaEstado,
            expiraDesde = query.ExpiraDesde,
            expiraHasta = query.ExpiraHasta,
            venceEnDias = query.VenceEnDias,
            standalone = query.Standalone,
            page = query.SafePage(),
            pageSize = query.SafePageSize(),
            textFilters = textFilters.Count > 0 ? textFilters : null,
        }, FiltersJsonOptions);
    }
}
