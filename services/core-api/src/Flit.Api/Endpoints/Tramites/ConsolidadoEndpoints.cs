using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Expediente consolidado (matrícula inicial y traspaso): fusiona FUR + adjuntos del trámite
/// en un PDF único (en traspaso incluye el contrato de compraventa).
/// </summary>
internal static class ConsolidadoEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesConsolidadoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapPost("/instances/{id:guid}/consolidado", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            // Bug #11139 — NULLABLE a propósito. Un `bool` de query sin `?` y sin valor por defecto es
            // OBLIGATORIO para Minimal APIs: omitirlo devuelve 400 antes siquiera de entrar al handler.
            // El asistente lo omite en su camino normal (solo manda ?force=true cuando regenera), así
            // que el expediente consolidado fallaba justo en el último paso del trámite. Se corrige en
            // el servidor y no en el cliente porque el defecto está en el contrato: omitir un flag
            // opcional debe dar el comportamiento normal, no un 400. Misma forma que el resto de la API.
            [FromQuery] bool? force,
            HttpContext http,
            // HU #12798 (AC4) — si la reconstrucción falla y hay consolidado anterior, se entrega ese con
            // el aviso en avisosCascada (y el fallo queda en la bitácora) en vez de un error sin documento.
            GenerarConsolidadoConRespaldoHandler handler,
            GeneracionDocumentalGestorGuard estadoGuard,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            // HU #11051 — con el trámite aprobado o anulado el expediente es definitivo: el gestor no lo
            // regenera. El OT sí sigue regenerándolo tras aprobar, por su propia ruta (AdminOtEndpoints),
            // que NO pasa por este gate.
            var estadoError = await estadoGuard.CheckAsync(id, tenantId.Value, ct);
            if (estadoError is not null)
                return GeneracionEstadoProblem.From(estadoError);

            // HU #11017 - el usuario habilita la generacion en cascada de la impronta (el proveedor la exige).
            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ResolveUserId(http.User), force ?? false, ct);
            // Éxito SOLO cuando no hay error. El comodín anterior (`_ => Created`) trataba cualquier
            // código NO MAPEADO como éxito y devolvía 201 con cuerpo nulo: el gestor veía la
            // operación "correcta", sin consolidado y sin motivo (p. ej. `organismo_requerido`).
            return error is null
                ? Results.Created($"/api/v1/tramites/instances/{id}/attachments", result)
                : ProblemFor(error);
        }).WithName("GenerarProcedureInstanceConsolidado");

        // HU #12785 — ruta ÚNICA de entrega (gestor y SuperAdmin con X-Tenant-Id): devuelve los metadatos
        // del consolidado vigente y lo reconstruye SOLO si la bandera está abajo. Estado final, PDF del
        // SuperAdmin (Source=user) y migrado V1 se entregan tal cual. El binario se baja luego por la
        // descarga / preview-url del adjunto con `document.attachmentId`. Sin GeneracionDocumentalGestorGuard
        // a propósito: en estado final la entrega NO genera, sirve el definitivo (AC3).
        group.MapGet("/instances/{id:guid}/consolidado/entrega", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            // `consolidado` (default) | `consolidado_maestro`.
            [FromQuery] string? tipo,
            // Nullable a propósito (Bug #11139): omitirlo = comportamiento normal.
            [FromQuery] bool? force,
            [FromQuery] bool? soloLectura,
            HttpContext http,
            EntregarConsolidadoHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");
            if (!TryParseTipo(tipo, ConsolidadoEntregaTipo.Wizard, out var tipoEntrega))
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "tipo debe ser 'consolidado' o 'consolidado_maestro'.");

            var (result, error) = await handler.HandleAsync(
                new EntregarConsolidadoRequest(
                    id, tenantId.Value, tipoEntrega, ResolveUserId(http.User), force ?? false, soloLectura ?? false),
                ct);
            return error switch
            {
                null => Results.Ok(result),
                EntregarConsolidadoHandler.ConsolidadoNoGenerado => Results.Problem(
                    statusCode: 404, title: "Not Found", detail: "El trámite no tiene consolidado generado."),
                _ => ProblemFor(error),
            };
        }).WithName("EntregarProcedureInstanceConsolidado");

        return app;
    }

    /// <summary>Parseo del query <c>tipo</c> de la ruta de entrega (HU #12785); null/vacío ⇒ <paramref name="porDefecto"/>.</summary>
    internal static bool TryParseTipo(string? tipo, ConsolidadoEntregaTipo porDefecto, out ConsolidadoEntregaTipo resultado)
    {
        resultado = porDefecto;
        if (string.IsNullOrWhiteSpace(tipo))
            return true;
        if (string.Equals(tipo, "consolidado", StringComparison.OrdinalIgnoreCase))
        {
            resultado = ConsolidadoEntregaTipo.Wizard;
            return true;
        }

        if (string.Equals(tipo, "consolidado_maestro", StringComparison.OrdinalIgnoreCase))
        {
            resultado = ConsolidadoEntregaTipo.Maestro;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Traducción de los códigos de error de los generadores de consolidado a ProblemDetails. La
    /// comparten el POST de generación y el GET de entrega (HU #12785) para que un mismo fallo se
    /// explique igual por las dos rutas.
    /// </summary>
    internal static IResult ProblemFor(string error) => error switch
    {
        "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
        "migrado_solo_lectura" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Trámite migrado (solo lectura): no se regenera el consolidado."),
        "modalidad_no_soportada" => Results.Problem(statusCode: 409, title: "Conflict", detail: "El consolidado solo está disponible para matrícula inicial y traspaso."),
        SubmitGate.FurRequerido => Results.Problem(statusCode: 409, title: "Conflict", detail: "Debe generar el FUR antes del consolidado."),
        "sin_adjuntos" => Results.Problem(statusCode: 409, title: "Conflict", detail: "No hay adjuntos para consolidar."),
        "adjunto_no_disponible" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto del expediente no está disponible en almacenamiento."),
        "mimetype_no_soportado" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Un adjunto tiene un formato no soportado para el consolidado."),
        "storage_unavailable" => Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "No se pudo guardar el consolidado en el almacenamiento de archivos. Intenta de nuevo en unos minutos."),
        "organismo_requerido" => Results.Problem(
            statusCode: 409,
            title: "Conflict",
            detail: "El organismo de tránsito del trámite no está seleccionado o no está activo "
                + "en el sistema. Verifícalo antes de generar el expediente consolidado."),
        // Cualquier otro código viaja tal cual: es mejor un motivo técnico que un éxito falso.
        _ => Results.Problem(
            statusCode: 409,
            title: "Conflict",
            detail: $"No se pudo generar el expediente consolidado: {error}."),
    };

    /// <summary>Usuario autenticado (claim <c>sub</c>), para la generación en cascada de la impronta.</summary>
    private static Guid? ResolveUserId(System.Security.Claims.ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
