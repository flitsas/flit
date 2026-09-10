using Flit.Admin.Application.Companies.Settings;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Admin.Domain.DocumentRequirements;
using Flit.Api.Authorization;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Tramites;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Flit.Api.Endpoints;

/// <summary>
/// SuperAdmin — Plataforma → Notificaciones → Plantillas: catálogo y render de muestra por id
/// (HU #11356, Feature #11347, última HU del Feature).
/// </summary>
/// <remarks>
/// <para>
/// Archivo y grupo de rutas PROPIOS, separados de <c>AdminPlataformaNotificacionesEndpoints</c>
/// (HU #11366, Feature #11349, buzón de pruebas) para no pisar el trabajo paralelo de ese Feature:
/// mismo espacio <c>/api/v1/admin/plataforma/notificaciones</c>, prefijo propio <c>/plantillas</c>.
/// </para>
/// <para>
/// Sin efectos observables (AC5/AC6): esta clase es 100% estática y solo consume
/// <see cref="NotificationTemplateCatalog"/> y las muestras de <c>Flit.Infrastructure.Notifications.Preview</c>
/// (que a su vez componen con los MISMOS composers de producción, pero con marcadores — nunca datos
/// reales). No referencia <c>IEmailSender</c>, <c>ITemporaryPasswordGenerator</c>,
/// <c>IPasswordHasher</c> ni <c>IAdminAuditWriter</c>: no hay forma de que un render de muestra
/// envíe correo, genere/persista contraseña o escriba auditoría — no porque se haya comprobado en
/// tiempo de ejecución, sino porque el código de este endpoint no tiene ninguna referencia con la
/// que hacerlo.
/// </para>
/// <para>
/// AC3 — el contrato de entrada de <c>GET .../plantillas/{templateId}/muestra</c> solo admite el id
/// de PLANTILLA (ruta) más dos parámetros de consulta opcionales (<c>tramiteId</c>, <c>usuarioId</c>)
/// cuyo ÚNICO rol es servir de superficie para que un identificador real intente colarse y sea
/// rechazado. Si cualquiera de los dos llega con valor, la respuesta es 400 ANTES de tocar el
/// catálogo — ni siquiera se evalúa si <c>templateId</c> existe. Eso evita el canal lateral por
/// tiempo/forma de respuesta: la petición con id de plantilla válido + tramiteId colado y la
/// petición con id de plantilla inválido + tramiteId colado devuelven EXACTAMENTE el mismo 400,
/// porque la validación del identificador real ocurre antes que cualquier resolución de catálogo o
/// composición. La ruta nunca consulta base de datos con esos parámetros: no existe repositorio de
/// trámites/usuarios inyectado en esta clase.
/// </para>
/// </remarks>
public static class AdminPlataformaNotificacionesPlantillasEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlataformaNotificacionesPlantillasEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/plataforma/notificaciones/plantillas")
            .RequireAuthorization(AdminAuthorization.SuperAdminPolicy)
            .WithTags("Admin · Plataforma · Notificaciones · Plantillas");

        group.MapGet("", ListAsync)
            .WithName("AdminPlataformaNotificacionesPlantillasList")
            .Produces<NotificationTemplateListResponse>(StatusCodes.Status200OK);

        group.MapGet("/{templateId}/muestra", GetSampleAsync)
            .WithName("AdminPlataformaNotificacionesPlantillasGetMuestra")
            .Produces<NotificationTemplateSampleResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    // AC1 — listado íntegro del catálogo, sin filtrar por módulo ni por lo que ya se implementó.
    private static IResult ListAsync()
    {
        var items = NotificationTemplateCatalog.All
            .Select(ToListItem)
            .ToList();

        return Results.Ok(new NotificationTemplateListResponse(items));
    }

    private static async Task<IResult> GetSampleAsync(
        string templateId,
        [FromQuery] string? tramiteId,
        [FromQuery] string? usuarioId,
        [FromQuery] string? channel,
        [FromQuery] Guid? procedureTypeId,
        IOptions<NotificationEmailAssetsOptions> emailAssets,
        IProcedureTypeCatalog procedureTypes,
        CancellationToken ct)
    {
        // AC3 — el contrato NO admite identificadores reales. Se rechaza aquí, ANTES de resolver
        // el catálogo, para que la respuesta sea IDÉNTICA exista o no exista el templateId
        // recibido (sin canal lateral por tiempo ni por forma del error).
        if (!string.IsNullOrWhiteSpace(tramiteId) || !string.IsNullOrWhiteSpace(usuarioId))
            return Results.BadRequest(new { error = "solicitud_invalida" });

        // Canal opcional: si llega, debe ser un valor wire válido. Por defecto Colas FLIT
        // (variante FLIT para plantillas Trámites con dual-canal).
        var resolvedChannel = NotificationChannel.FlitSmtp;
        if (!string.IsNullOrWhiteSpace(channel))
        {
            if (!SettingsWire.TryParseChannel(channel, out resolvedChannel))
                return Results.BadRequest(new { error = "canal_invalido", allowed = SettingsWire.AllowedChannels });
        }

        if (!NotificationTemplateCatalog.TryResolve(templateId ?? string.Empty, out var descriptor))
            return Results.NotFound();

        NotificationSampleProcedureType? overlay = null;
        if (TramiteCambioEstadoEmailComposer.RequiresProcedureType(descriptor.Id))
        {
            if (procedureTypeId is null || procedureTypeId == Guid.Empty)
                return Results.BadRequest(new { error = "procedure_type_id_invalido" });

            var type = await procedureTypes
                .GetByIdForNotificationPreviewAsync(procedureTypeId.Value, ct)
                .ConfigureAwait(false);
            if (type is null)
                return Results.BadRequest(new { error = "tipo_tramite_no_encontrado" });
            if (!type.IsActive)
                return Results.BadRequest(new { error = "tipo_tramite_inactivo" });

            overlay = new NotificationSampleProcedureType(
                type.Name,
                string.Equals(type.Family, "TRASPASO", StringComparison.OrdinalIgnoreCase));
        }

        var assetsBaseUrl = emailAssets.Value.BaseUrl;
        var (subject, html) = NotificationSampleRenderer.Render(
            descriptor.Id, resolvedChannel, assetsBaseUrl, overlay);
        return Results.Ok(new NotificationTemplateSampleResponse(descriptor.Id, subject, html));
    }

    private static NotificationTemplateListItemResponse ToListItem(NotificationTemplateDescriptor descriptor) =>
        new(
            descriptor.Id,
            descriptor.Name,
            descriptor.Module.ToString(),
            descriptor.Triggers.Select(t => t.ToString()).ToList());
}

/// <summary>Respuesta de <c>GET /api/v1/admin/plataforma/notificaciones/plantillas</c> (AC1).</summary>
public sealed record NotificationTemplateListResponse(IReadOnlyList<NotificationTemplateListItemResponse> Items);

/// <summary>Entrada del catálogo expuesta al panel (AC1) — id estable, módulo y disparadores.</summary>
public sealed record NotificationTemplateListItemResponse(
    string Id,
    string Name,
    string Module,
    IReadOnlyList<string> Triggers);

/// <summary>
/// Respuesta de <c>GET /api/v1/admin/plataforma/notificaciones/plantillas/{templateId}/muestra</c>
/// (AC2) — asunto y cuerpo HTML compuestos en modo muestra, sin datos de personas (Ley 1581).
/// </summary>
public sealed record NotificationTemplateSampleResponse(string TemplateId, string Subject, string Html);
