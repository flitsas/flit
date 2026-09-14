using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Tramites.Application.Auditing;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12410 (Feature #12257, épica #12235) — documentos de un trámite de la red, en solo lectura y
/// por canal proxeado, bajo el MISMO grupo <c>/api/v1/tramites/network</c> (policy de cabeza
/// <see cref="GroupHeadReadFilter"/> + auditoría <see cref="NetworkAccessAuditFilter"/> heredadas):
/// <list type="bullet">
///   <item><c>GET /instances/{id}/attachments</c> — metadatos (<see cref="AttachmentsResponse"/>, el
///   mismo <see cref="AttachmentDto"/> del propio hijo). Sin <c>preview-url</c>, sin <c>storagePath</c>,
///   sin dirección prefirmada alguna (AC1).</item>
///   <item><c>GET /instances/{id}/attachments/{attachmentId}/download</c> — el binario por transmisión
///   con <c>Content-Disposition: attachment</c>, resolviendo el tenant dueño del trámite y exigiendo
///   que esté en el conjunto de lectura de la cabeza (AC2). No existe <c>preview-url</c> de red: la
///   URL prefirmada de S3 es un portador anónimo sin cliente ni usuario en la firma.</item>
/// </list>
/// Códigos: 403 <c>network_scope_required</c> (sin alcance de grupo, incl. interruptor
/// <c>group_read_scope</c> apagado — AC7) · 403 <c>network_documents_disabled</c> (cabeza CONCESION con
/// <c>network_documents_concesion</c> apagado — AC5) · 404 <c>{ error: "not_found" }</c> idéntico para
/// trámite inexistente, ajeno, fuera del alcance, documento inexistente o binario perdido (AC6).
/// Cada listado y cada descarga (servida o rechazada sobre un hijo) publica UN desenlace con el
/// trámite, el dueño y el documento para <c>tramites.network_access_audit</c> (AC4/AC9). Toda escritura
/// sobre documentos sigue rechazada por <see cref="TenantWriteGuard"/> en las rutas de siempre (AC3).
/// </summary>
internal static class NetworkAttachmentEndpoints
{
    /// <summary>
    /// Policy explícita de las dos rutas (hallazgo de seguridad del PR #370): el slug RBAC de «Ver
    /// trámites», el mismo que habilita el módulo de Operación al usuario. Se eligió sobre
    /// <c>GroupHeadCompanyPolicy</c>/<c>AdminCompanyPolicy</c> porque ambas exigen el rol AdminCompany y
    /// la vista consolidada está abierta a TODO usuario de la cabeza (el selector de alcance del
    /// frontend se pinta por <c>is_group_parent</c>, no por rol): un operador de la cabeza que ve el
    /// detalle del hijo debe poder ver sus documentos. SuperAdmin hace bypass del permiso y aun así
    /// recibe 403 <c>network_scope_required</c> de <see cref="GroupHeadReadFilter"/> (D7). Un token sin
    /// el slug ⇒ 403 antes de tocar el alcance, el interruptor o el almacenamiento, y sin auditar
    /// (el actor no llegó a la ruta).
    /// </summary>
    internal const string ReadPermission = "tramites.read";

    internal static RouteGroupBuilder MapNetworkAttachments(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/instances/{id:guid}/attachments", async (
            Guid id,
            HttpContext http,
            NetworkAttachmentsHandler handler,
            CancellationToken ct) =>
        {
            var outcome = await handler.ListAsync(id, RequestTenantResolver.ScopeFromItems(http), ct);
            Publish(http, NetworkAccessVocabulary.Resources.AttachmentsList, id, attachmentId: null, outcome.ProcedureTenantId, outcome.Error);
            return outcome.Error switch
            {
                null => Results.Ok(outcome.Result),
                NetworkDocumentsPolicy.NotFound => NotFound(),
                _ => Forbidden(outcome.Error),
            };
        })
            .RequirePermission(ReadPermission)
            .WithName("NetworkListProcedureInstanceAttachments")
            .WithSummary("Metadatos de los documentos de un trámite de la red (solo lectura, sin URLs)")
            .Produces<AttachmentsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/instances/{id:guid}/attachments/{attachmentId:guid}/download", async (
            Guid id,
            Guid attachmentId,
            HttpContext http,
            NetworkAttachmentsHandler handler,
            CancellationToken ct) =>
        {
            var outcome = await handler.DownloadAsync(id, attachmentId, RequestTenantResolver.ScopeFromItems(http), ct);
            Publish(http, NetworkAccessVocabulary.Resources.AttachmentsDownload, id, attachmentId, outcome.ProcedureTenantId, outcome.Error);
            return outcome.Error switch
            {
                // Results.File con fileDownloadName => Content-Disposition: attachment (igual que la ruta propia).
                null => Results.File(outcome.Result!.Content, outcome.Result.Mimetype, outcome.Result.Filename),
                NetworkDocumentsPolicy.NotFound => NotFound(),
                _ => Forbidden(outcome.Error),
            };
        })
            .RequirePermission(ReadPermission)
            .WithName("NetworkDownloadProcedureInstanceAttachment")
            .WithSummary("Descarga proxeada (transmisión) de un documento de un trámite de la red")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return group;
    }

    /// <summary>
    /// Un desenlace por petición para el auditor (HU #12361): solo cuando se resolvió el dueño del
    /// trámite (servido ⇒ <c>ok</c>; interruptor/clase ⇒ <c>forbidden</c>; documento ausente o trámite
    /// fuera del alcance ⇒ <c>not_found</c>). Un id inexistente no tiene hijo al que imputar el acceso y
    /// no publica nada; el filtro descarta a la propia cabeza (AC5 de #12361).
    /// </summary>
    private static void Publish(HttpContext http, string resource, Guid procedureId, Guid? attachmentId, Guid? owner, string? error)
    {
        if (owner is not { } tenantId)
            return;

        var result = error switch
        {
            null => NetworkAccessVocabulary.Results.Ok,
            NetworkDocumentsPolicy.NotFound => NetworkAccessVocabulary.Results.NotFound,
            _ => NetworkAccessVocabulary.Results.Forbidden,
        };
        NetworkAccessAuditContext.Publish(http, new NetworkAccessOutcome(
            resource, result, [tenantId], ProcedureId: procedureId, ProcedureTenantId: tenantId, AttachmentId: attachmentId));
    }

    /// <summary>404 escueto (patrón de generación documental): un solo cuerpo para todos los casos.</summary>
    private static IResult NotFound() =>
        Results.Json(new { error = NetworkDocumentsPolicy.NotFound }, statusCode: StatusCodes.Status404NotFound);

    private static IResult Forbidden(string error) =>
        Results.Json(new { error }, statusCode: StatusCodes.Status403Forbidden);
}
