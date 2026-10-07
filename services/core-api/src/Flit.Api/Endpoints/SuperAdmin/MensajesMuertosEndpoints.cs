using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Flit.Api.Endpoints.Auditing;
using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.Endpoints.SuperAdmin;

/// <summary>
/// Mensajes muertos de Notificaciones (Epic #13316, HU #13357): los correos y webhooks que agotaron sus reintentos.
/// El SuperAdmin los lista, los reintenta (vuelven a su cola y se procesan) o los descarta; reintentar y descartar quedan
/// en la auditoría administrativa. Solo SuperAdmin (lo exige el grupo: un AdminCompany recibe 403). Los mensajes viven
/// en el broker; core-api los administra por gRPC con core-notificaciones (obligatorio desde el corte, HU #13359).
/// </summary>
internal static class MensajesMuertosEndpoints
{
    private const string Entidad = "dead_letter";
    private const string Objetivo = "DEAD_LETTER";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/notificaciones/mensajes-muertos", ListarAsync).WithName("ListMensajesMuertos");

        group.MapPost("/notificaciones/mensajes-muertos/{cola}/{mensajeId:guid}/reintentar", ReintentarAsync)
            .WithName("RetryMensajeMuerto")
            .AddEndpointFilter(new AdminAuditFilter(AuditVocabulary.Modules.Notifications, AuditVocabulary.Operations.RetryDeadLetter, Entidad, Objetivo, "mensajeId"));

        group.MapPost("/notificaciones/mensajes-muertos/{cola}/{mensajeId:guid}/descartar", DescartarAsync)
            .WithName("DiscardMensajeMuerto")
            .AddEndpointFilter(new AdminAuditFilter(AuditVocabulary.Modules.Notifications, AuditVocabulary.Operations.DiscardDeadLetter, Entidad, Objetivo, "mensajeId"));
    }

    private static async Task<IResult> ListarAsync(
        string? cola, int? limite, HttpContext http, MensajesMuertosService.MensajesMuertosServiceClient cliente, CancellationToken ct)
    {
        if (Cola(cola) is not { } c)
            return ColaInvalida();

        return await LlamarAsync(async metadata =>
        {
            var r = await cliente.ListarMensajesMuertosAsync(
                new ListarMensajesMuertosRequest { Cola = c, Limite = limite ?? 100 }, metadata, cancellationToken: ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                mensajes = r.Mensajes.Select(m => new
                {
                    id = m.Id,
                    tipo = m.Tipo,
                    empresaId = m.HasTenantId ? m.TenantId : null,
                    origen = m.HasProductor ? m.Productor : null,
                    ocurridoEn = m.OcurridoEn?.ToDateTimeOffset(),
                    muertoEn = m.MuertoEn?.ToDateTimeOffset(),
                    motivo = m.HasMotivo ? m.Motivo : null,
                    ultimoError = m.HasUltimoError ? m.UltimoError : null,
                    intentos = m.Intentos,
                }),
            });
        }, http).ConfigureAwait(false);
    }

    private static Task<IResult> ReintentarAsync(
        string cola, Guid mensajeId, HttpContext http, MensajesMuertosService.MensajesMuertosServiceClient cliente, CancellationToken ct) =>
        AccionAsync(cola, http, cliente, (cliente, c, metadata) => cliente.ReintentarMensajeMuertoAsync(
            new ReintentarMensajeMuertoRequest { Cola = c, Id = mensajeId.ToString() }, metadata, cancellationToken: ct).ResponseAsync);

    private static Task<IResult> DescartarAsync(
        string cola, Guid mensajeId, HttpContext http, MensajesMuertosService.MensajesMuertosServiceClient cliente, CancellationToken ct) =>
        AccionAsync(cola, http, cliente, (cliente, c, metadata) => cliente.DescartarMensajeMuertoAsync(
            new DescartarMensajeMuertoRequest { Cola = c, Id = mensajeId.ToString() }, metadata, cancellationToken: ct).ResponseAsync);

    private static async Task<IResult> AccionAsync<T>(
        string cola, HttpContext http, MensajesMuertosService.MensajesMuertosServiceClient cliente,
        Func<MensajesMuertosService.MensajesMuertosServiceClient, ColaMuertos, Metadata, Task<T>> accion)
    {
        if (Cola(cola) is not { } c)
            return ColaInvalida();

        return await LlamarAsync(async metadata =>
        {
            await accion(cliente, c, metadata).ConfigureAwait(false);
            return Results.NoContent();
        }, http).ConfigureAwait(false);
    }

    private static async Task<IResult> LlamarAsync(Func<Metadata, Task<IResult>> llamada, HttpContext http)
    {
        // La empresa de quien administra (los mensajes son de todas las empresas); la exige el contrato de servicio.
        var empresa = RequestTenantResolver.ResolveTenantIdOrNull(http.User) ?? Guid.Empty;
        if (empresa == Guid.Empty)
            return Problem(StatusCodes.Status400BadRequest, "SIN_EMPRESA", "El usuario no tiene empresa en su sesión.");
        try
        {
            return await llamada(new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, empresa.ToString() } }).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Problem(StatusCodes.Status404NotFound, "MENSAJE_NO_ENCONTRADO", "El mensaje ya no está en la cola de mensajes muertos.");
        }
        catch (RpcException ex)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "NOTIFICACIONES_NO_DISPONIBLE", $"Notificaciones respondió {ex.StatusCode}.");
        }
    }

    private static ColaMuertos? Cola(string? cola) => cola switch
    {
        "correos" => ColaMuertos.Correos,
        "webhooks" => ColaMuertos.Webhooks,
        _ => null,
    };

    private static IResult ColaInvalida() =>
        Problem(StatusCodes.Status400BadRequest, "COLA_INVALIDA", "La cola es «correos» o «webhooks».");

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
