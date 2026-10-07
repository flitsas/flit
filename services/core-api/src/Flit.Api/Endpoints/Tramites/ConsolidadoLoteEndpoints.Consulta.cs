using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D4/D7/D8) — rutas neutras de consulta y descarga del lote:
/// <list type="bullet">
///   <item><c>GET /api/v1/consolidados/lotes/actual</c> → 200 <c>LoteConsolidados</c> (activo o último retenido, con
///   <c>expiraEn</c> y sus partes) o 204.</item>
///   <item><c>GET /api/v1/consolidados/lotes/{loteId}</c> → 200 o 404.</item>
///   <item><c>GET /api/v1/consolidados/lotes/{loteId}/partes/{numero}</c> → 200 <c>application/zip</c> en streaming
///   (storage → descifrado → respuesta) con <c>Content-Length</c> = <c>plain_size_bytes</c> y
///   <c>Content-Disposition: attachment</c>; 404 / 409 <c>lote_no_terminado</c> / 410 <c>descarga_expirada</c> /
///   503 <c>auditoria_no_registrada</c> / 500 <c>parte_no_disponible</c>, todos sin bytes del ZIP.</item>
/// </list>
/// <b>I1 — dueño = <c>sub</c></b>: las tres filtran por <c>requested_by_user_id</c> e ignoran <c>X-Tenant-Id</c>; no
/// están en <c>TenantEnforcementMiddleware.RuntimeScopedRoutes</c> (el <c>ot_admin</c>, con tenant OT, consulta y descarga
/// su lote OT). Otro usuario, incluido el Super Admin (que pasa <c>RequirePermission</c> por bypass), recibe 404.
/// Permiso <see cref="ConsolidadoLotePermisos.Descargar"/> en las tres (CF-18).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// GET /api/v1/consolidados/lotes/0199…/partes/1
/// → 200 application/zip; Content-Length: 104857600;
///   Content-Disposition: attachment; filename="consolidados_20261006_1430_parte-01-de-04.zip"
/// </code>
/// </remarks>
internal static partial class ConsolidadoLoteEndpoints
{
    internal const string MensajeNoTerminado = "El lote todavía no terminó; las partes se descargan cuando termine.";
    internal const string MensajeExpirada = "La descarga expiró. Solicita de nuevo la descarga masiva.";
    internal const string MensajeAuditoria = "No se pudo completar la descarga, intente de nuevo.";
    internal const string MensajeParteNoDisponible = "No se pudo leer la parte del lote, intente de nuevo.";

    internal static IEndpointRouteBuilder MapConsolidadoLoteConsultaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{RutaLotes}/actual", ActualAsync)
            .RequirePermission(ConsolidadoLotePermisos.Descargar)
            .WithName("ObtenerLoteConsolidadosActual")
            .WithSummary("Lote activo del usuario o el último terminal aún retenido")
            .Produces<LoteConsolidadosDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet($"{RutaLotes}/{{loteId:guid}}", PorIdAsync)
            .RequirePermission(ConsolidadoLotePermisos.Descargar)
            .WithName("ObtenerLoteConsolidados")
            .WithSummary("Lote por id (solo del dueño)")
            .Produces<LoteConsolidadosDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet($"{RutaLotes}/{{loteId:guid}}/partes/{{numero:int}}", DescargarParteAsync)
            .RequirePermission(ConsolidadoLotePermisos.Descargar)
            .WithName("DescargarParteLoteConsolidados")
            .WithSummary("Descarga una parte del lote (descifrado en streaming; nunca genera PDF)")
            .Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ActualAsync(HttpContext http, ConsultarLoteConsolidadosHandler handler, CancellationToken ct)
    {
        if (UsuarioDelToken(http.User) is not { } usuarioId)
            return Results.Unauthorized();

        var r = await handler.ActualAsync(new ObtenerLoteActualQuery(usuarioId), ct);
        return r is null ? Results.NoContent() : Results.Ok(Dto(r));
    }

    private static async Task<IResult> PorIdAsync(
        HttpContext http, Guid loteId, ConsultarLoteConsolidadosHandler handler, CancellationToken ct)
    {
        if (UsuarioDelToken(http.User) is not { } usuarioId)
            return Results.Unauthorized();

        var r = await handler.PorIdAsync(new ObtenerLoteQuery(loteId, usuarioId), ct);
        return r is null ? Results.NotFound() : Results.Ok(Dto(r));
    }

    private static async Task<IResult> DescargarParteAsync(
        HttpContext http, Guid loteId, int numero, DescargarParteHandler handler, ILoggerFactory loggers, CancellationToken ct)
    {
        if (UsuarioDelToken(http.User) is not { } usuarioId)
            return Results.Unauthorized();

        var query = new DescargarParteQuery(
            loteId,
            numero,
            usuarioId,
            RolDelSolicitante(http.User, CompanyTenantAccess.IsSuperAdmin(http.User)),
            http.Connection.RemoteIpAddress,
            UserAgent(http));
        var r = await handler.PrepararAsync(query, ct);
        return r.Estado switch
        {
            DescargarParteEstado.Lista => new ParteZipResult(r.Descarga!, handler,
                loggers.CreateLogger(typeof(ConsolidadoLoteEndpoints).FullName!)),
            DescargarParteEstado.NoTerminado => Problema(StatusCodes.Status409Conflict,
                ConsolidadoLoteErrores.LoteNoTerminado, MensajeNoTerminado),
            DescargarParteEstado.Expirada => Problema(StatusCodes.Status410Gone,
                ConsolidadoLoteErrores.DescargaExpirada, MensajeExpirada),
            DescargarParteEstado.AuditoriaNoRegistrada => Problema(StatusCodes.Status503ServiceUnavailable,
                ConsolidadoLoteErrores.AuditoriaNoRegistrada, MensajeAuditoria),
            DescargarParteEstado.ParteNoDisponible => Problema(StatusCodes.Status500InternalServerError,
                ConsolidadoLoteErrores.ParteNoDisponible, MensajeParteNoDisponible),
            _ => Results.NotFound(),
        };
    }

    private static LoteConsolidadosDto Dto(LoteConsultado r) =>
        LoteConsolidadosDto.Desde(
            r.Lote,
            [.. r.Partes.Select(p => new ParteLoteConsolidadosDto(p.Numero, p.NombreArchivo, p.Pdfs, p.Omitidos, p.Bytes))],
            r.NombreBase);

    /// <summary>
    /// Escribe la parte en la respuesta. Cabeceras (200, <c>application/zip</c>, <c>Content-Length</c>,
    /// <c>Content-Disposition</c>, <c>no-store</c>) antes del primer byte; el cuerpo lo escribe el descifrador bloque a
    /// bloque, sin búfer de respuesta. Ante un fallo: si aún no salió ningún byte, 500 <c>parte_no_disponible</c> limpio;
    /// si ya salieron, <see cref="HttpContext.Abort"/> — el cliente ve la conexión cortada (y menos bytes que el
    /// <c>Content-Length</c>), nunca un ZIP truncado como válido.
    /// </summary>
    internal sealed partial class ParteZipResult(DescargaParte descarga, DescargarParteHandler handler, ILogger logger) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            await using var _ = descarga;
            var response = httpContext.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "application/zip";
            response.ContentLength = descarga.BytesEnClaro;
            // El nombre solo lleva [A-Za-z0-9_-.] (ConsolidadoLoteNombres.Zip): no necesita escaparse.
            response.Headers.ContentDisposition = $"attachment; filename=\"{descarga.NombreArchivo}\"";
            response.Headers.CacheControl = "no-store";
            response.Headers.XContentTypeOptions = "nosniff";
            httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            var ct = httpContext.RequestAborted;
            try
            {
                await handler.EscribirAsync(descarga, response.Body, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // El cliente cortó la descarga: no hay a quién responder.
                LogClienteCorto(logger, descarga.Lote.Id, descarga.Parte.PartNumber);
            }
#pragma warning disable CA1031 // Cualquier fallo de lectura o descifrado se traduce en 500 sin bytes o en conexión abortada.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFalloDescarga(logger, descarga.Lote.Id, descarga.Parte.PartNumber, ex.GetType().Name, response.HasStarted);
                if (response.HasStarted)
                {
                    httpContext.Abort();
                    return;
                }

                response.Clear();
                await Problema(StatusCodes.Status500InternalServerError, ConsolidadoLoteErrores.ParteNoDisponible,
                    MensajeParteNoDisponible).ExecuteAsync(httpContext);
            }
        }

        [LoggerMessage(Level = LogLevel.Information,
            Message = "Lote {LoteId}: el cliente cortó la descarga de la parte {PartNumber}.")]
        private static partial void LogClienteCorto(ILogger logger, Guid loteId, short partNumber);

        [LoggerMessage(Level = LogLevel.Error,
            Message = "Lote {LoteId}: la descarga de la parte {PartNumber} falló ({Tipo}); respuesta iniciada={Iniciada} (iniciada ⇒ conexión abortada).")]
        private static partial void LogFalloDescarga(ILogger logger, Guid loteId, short partNumber, string tipo, bool iniciada);
    }
}
