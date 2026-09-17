using Flit.Admin.Domain.Companies;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12555 (Feature #12257, épica #12235) — listado NO-admin de los clientes hijos vigentes de la
/// cabeza de grupo (<c>GET /children</c>), para selectores de la red (Concesión / Marca Blanca) sin
/// exigir la policy de administración que sí requiere <see cref="AdminCompanyChildrenEndpoints"/>
/// (<c>AdminAuthorization.GroupHeadCompanyPolicy</c>).
/// <para>
/// Vive dentro del grupo <c>/api/v1/tramites/network</c> (<see cref="NetworkProcedureEndpoints"/>):
/// hereda <see cref="GroupHeadReadFilter"/> (403 <c>network_scope_required</c> para <c>Single</c>,
/// SuperAdmin o sin alcance — AC2) y <see cref="Auditing.NetworkAccessAuditFilter"/> sin duplicar la
/// validación aquí. El alcance sale SIEMPRE de <see cref="RequestTenantResolver.ScopeFromItems"/> (BD
/// vía middleware); ninguna cabecera ni parámetro de la petición lo amplía (AC4).
/// </para>
/// <para>
/// Reutiliza <see cref="ICompanyHierarchyRepository.ListChildrenAsync"/> — el mismo query que
/// <see cref="AdminCompanyChildrenEndpoints"/> (<c>parent_tenant_id = headTenantId</c>, así que un
/// hijo recién desvinculado deja de aparecer en la siguiente petición sin caché — AC3) — en vez de
/// duplicar la consulta, y proyecta solo <c>id</c>/<c>nombre</c>: el mínimo necesario para un selector,
/// sin NIT, código, estado ni fechas.
/// </para>
/// <para>
/// <b>Sin auditoría de red (HU #12361):</b> el recurso no expone datos de trámites ni PII de titulares,
/// solo las razones sociales de los propios clientes hijos de la cabeza. El endpoint no llama a
/// <see cref="Auditing.NetworkAccessAuditContext.Publish"/>, así que <see cref="Auditing.NetworkAccessAuditFilter"/>
/// no encuentra desenlace publicado y no escribe fila (comportamiento no-op ya soportado por el filtro
/// para rutas del grupo que no publican outcome) — decisión consistente con que este listado es
/// metadatos propios de la cabeza, no un acceso a los datos operativos de un hijo.
/// </para>
/// </summary>
internal static class NetworkChildrenEndpoints
{
    internal static RouteGroupBuilder MapNetworkChildren(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/children", async (
            HttpContext http,
            ICompanyHierarchyRepository hierarchy,
            CancellationToken ct) =>
        {
            var scope = RequestTenantResolver.ScopeFromItems(http);
            // GroupHeadReadFilter ya rechazó (403 network_scope_required) cualquier petición cuyo
            // alcance no sea una cabeza de grupo, así que aquí scope.IsGroup y scope.WriteTenantId
            // (el padre) siempre tienen valor.
            var headTenantId = scope!.WriteTenantId!.Value;

            var children = await hierarchy.ListChildrenAsync(headTenantId, ct).ConfigureAwait(false);
            var options = children
                .Select(c => new NetworkChildOption(c.Id, c.RazonSocial))
                .OrderBy(c => c.Nombre, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(options);
        })
            .WithName("NetworkChildren")
            .WithSummary("Clientes hijos vigentes de la cabeza de grupo (id + nombre), sin permiso admin")
            .Produces<IReadOnlyList<NetworkChildOption>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return group;
    }
}

/// <summary>
/// Proyección mínima de un hijo de red para selectores (HU #12555): sin NIT, código ni fechas —
/// el mínimo necesario del contrato acordado con el orquestador.
/// </summary>
internal sealed record NetworkChildOption(Guid Id, string Nombre);
