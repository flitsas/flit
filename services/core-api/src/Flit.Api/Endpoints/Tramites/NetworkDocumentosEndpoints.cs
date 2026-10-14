using Flit.Api.Authorization;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Ajuste #13419 AC5 (HU #13417, épica #13216) — <c>GET /documentos</c> dentro del grupo
/// <c>/api/v1/tramites/network</c> (<see cref="NetworkProcedureEndpoints"/>): dice a la vista de red si la cabeza puede
/// leer documentos de su red, para no ofrecer «Descargar ZIP» cuando el alta del lote respondería 403
/// <c>network_documents_disabled</c>. Hasta ahora solo lo sabía el Super Admin
/// (<c>GET /api/v1/admin/platform/hierarchy-switches</c>).
/// <para>
/// Hereda las puertas del grupo sin repetirlas, igual que <see cref="NetworkChildrenEndpoints"/>:
/// <see cref="GroupHeadReadFilter"/> (403 <c>network_scope_required</c> / <c>network_role_required</c>) y la pertenencia a
/// <c>TenantEnforcementMiddleware.RuntimeScopedRoutes</c> por prefijo. La decisión es
/// <see cref="NetworkDocumentsPolicy.IsAvailableAsync"/> — la misma regla y el mismo lector de interruptores que el
/// alta del lote (<c>LoteAlcanceRedPolicy</c>) y los documentos de un trámite de la red —, sobre el alcance que resolvió
/// el middleware desde la BD; nada de la petición lo cambia.
/// </para>
/// <para>
/// <b>Sin auditoría de red (P3 = b):</b> no publica desenlace en <see cref="Auditing.NetworkAccessAuditContext"/>, así que
/// <see cref="Auditing.NetworkAccessAuditFilter"/> no escribe fila: es una bandera de configuración de la propia cabeza,
/// no un acceso a datos de una hija.
/// </para>
/// </summary>
/// <remarks>Uso de ejemplo: <c>GET /api/v1/tramites/network/documentos</c> ⇒ <c>200 { "documentosRed": false }</c>.</remarks>
internal static class NetworkDocumentosEndpoints
{
    internal static RouteGroupBuilder MapNetworkDocumentos(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/documentos", async (HttpContext http, IHierarchySwitches switches, CancellationToken ct) =>
        {
            // GroupHeadReadFilter ya rechazó cualquier alcance que no sea el de una cabeza de grupo.
            var scope = RequestTenantResolver.ScopeFromItems(http)!;
            var disponibles = await NetworkDocumentsPolicy.IsAvailableAsync(scope, switches, ct).ConfigureAwait(false);
            return Results.Ok(new NetworkDocumentosDisponibilidad(disponibles));
        })
            .WithName("TramitesNetworkGetDocumentos")
            .WithSummary("¿La cabeza de grupo puede leer documentos de su red? (habilita «Descargar ZIP» en la vista de red)")
            .Produces<NetworkDocumentosDisponibilidad>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return group;
    }
}

/// <summary>Respuesta de <c>GET /network/documentos</c> (esquema OpenAPI <c>NetworkDocumentosDisponibilidad</c>).</summary>
internal sealed record NetworkDocumentosDisponibilidad(bool DocumentosRed);
