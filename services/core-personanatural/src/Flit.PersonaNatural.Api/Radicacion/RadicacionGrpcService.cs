using System.Text.Json;
using Flit.PersonaNatural.Api.Definiciones;
using Flit.PersonaNatural.Api.Persistence;
using Flit.PersonaNatural.Api.Pii;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.PersonaNatural.Grpc.V1;
using SolicitudesService = Flit.PersonaNatural.Api.Solicitudes.SolicitudesService;
using Flit.Platform.Sdk.Grpc;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.PersonaNatural.Api.Radicacion;

/// <summary>
/// <c>flit.personanatural.v1.RadicacionService</c> (HU #14049, Épica #14021): lectura POR PULL de lo que core-api necesita
/// para crear el trámite de una solicitud radicada. El evento de radicación no lleva datos personales; los datos de las
/// partes salen únicamente por aquí.
/// <list type="bullet">
///   <item>Token de servicio del cliente <see cref="ClienteAutorizado"/> con <c>aud=personanatural</c> y scope
///   <see cref="Scope"/> (lo valida el interceptor del SDK; el cliente lo valida este servicio) y la empresa de la solicitud.</item>
///   <item>Solo solicitudes <c>en_cola</c>: con otro estado, FAILED_PRECONDITION (sin leer ni auditar datos personales).</item>
///   <item>Cada lectura queda en <c>acceso_pii_log</c> (actor <see cref="Actor"/>, acción <c>lectura_para_radicar</c>, solicitud
///   y hora; nunca los datos) porque los datos personales salen solo por <see cref="PersonaService.LeerAsync"/>.</item>
///   <item>Nada de datos personales en logs ni en mensajes de error.</item>
/// </list>
/// </summary>
internal sealed class RadicacionGrpcService(PersonaNaturalDb db, PersonaService personas) : Flit.PersonaNatural.Grpc.V1.RadicacionService.RadicacionServiceBase
{
    /// <summary>Scope que debe traer el token para leer solicitudes para radicar; core-identity lo asigna al cliente de core-api.</summary>
    public const string Scope = "platform.personanatural.leer";

    /// <summary>Único cliente de servicio que puede leer: el de core-api (Trámites). Otro cliente, aunque tenga el scope, se deniega.</summary>
    public const string ClienteAutorizado = "svc-tramites";

    /// <summary>Quién queda como actor en la auditoría de acceso a datos personales.</summary>
    public const string Actor = "core-api";

    public const string ReasonNoEncontrada = "solicitud_no_encontrada";
    public const string ReasonNoRadicable = "solicitud_no_radicable";

    private static readonly JsonSerializerOptions JsonOpciones = new(JsonSerializerDefaults.Web);

    public override async Task<ObtenerSolicitudParaRadicarResponse> ObtenerSolicitudParaRadicar(ObtenerSolicitudParaRadicarRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var llamador = PlatformServiceCaller.From(context);
        if (!string.Equals(llamador.ClientId, ClienteAutorizado, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Este cliente no puede leer solicitudes para radicar."));
        if (!Guid.TryParse(request.SolicitudId, out var id) || id == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "solicitud_id debe ser un UUID."));

        var ct = context.CancellationToken;
        var s = await db.Set<SolicitudTramite>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == llamador.TenantId, ct).ConfigureAwait(false)
            ?? throw Error(StatusCode.NotFound, ReasonNoEncontrada, "No existe la solicitud.");
        if (s.Estado != EstadoSolicitud.EnCola)
            throw Error(StatusCode.FailedPrecondition, ReasonNoRadicable, "La solicitud no está en cola para radicar.");

        // Única puerta de lectura de datos personales: descifra y deja la auditoría (actor, acción, solicitud, hora).
        var partesClaro = await personas.LeerAsync(id, Actor, ct, PersonaService.AccionLecturaParaRadicar).ConfigureAwait(false);
        var validaciones = await db.Set<PersonaSolicitante>().AsNoTracking().Where(p => p.SolicitudId == id)
            .Select(p => new { p.Parte, p.Ordinal, p.ValidacionId, p.ValidacionEstado }).ToListAsync(ct).ConfigureAwait(false);
        var adjuntos = await db.Set<Adjunto>().AsNoTracking().Where(a => a.SolicitudId == id).OrderBy(a => a.CreatedAt).ToListAsync(ct).ConfigureAwait(false);

        var respuesta = new ObtenerSolicitudParaRadicarResponse
        {
            SolicitudId = s.Id.ToString(),
            CodigoPublico = s.CodigoPublico,
            TipoTramiteCodigo = s.TipoTramiteCodigo,
            DefinicionVersion = s.DefinicionVersion,
            DefinicionHuella = DefinicionesHuella(s),
            OrganismoId = s.OrganismoId.ToString(),
            OrganismoNombre = s.OrganismoNombre,
            Placa = s.Placa ?? string.Empty,
            Vin = s.Vin ?? string.Empty,
            DatosJson = DatosSolicitud.Leer(s.Datos).Serializar(),
            ComprobantePagoId = adjuntos.LastOrDefault(a => a.Tipo == SolicitudesService.TipoComprobantePago)?.Id.ToString() ?? string.Empty,
            IdempotencyKey = s.IdempotencyKey ?? string.Empty,
            CorrelationId = s.CorrelationId.ToString(),
        };
        foreach (var (clave, valor) in LeerCampos(s.FieldValues))
            respuesta.FieldValues[clave] = valor;
        foreach (var p in partesClaro)
        {
            var v = validaciones.FirstOrDefault(x => x.Parte == p.Parte && x.Ordinal == p.Ordinal);
            respuesta.Partes.Add(new ParteSolicitud
            {
                Rol = p.Parte,
                Ordinal = p.Ordinal,
                TipoDocumento = p.TipoDocumento,
                NumeroDocumento = p.Documento,
                Nombre = p.Nombre ?? string.Empty,
                Correo = p.Correo ?? string.Empty,
                Celular = p.Celular ?? string.Empty,
                Direccion = p.Direccion ?? string.Empty,
                ValidacionId = v?.ValidacionId?.ToString() ?? string.Empty,
                ValidacionEstado = v?.ValidacionEstado ?? string.Empty,
            });
        }

        foreach (var a in adjuntos)
        {
            respuesta.Adjuntos.Add(new AdjuntoSolicitud
            {
                Id = a.Id.ToString(),
                Tipo = a.Tipo,
                Referencia = a.StoragePath,
                Sha256 = a.Sha256,
                TamanoBytes = a.TamanoBytes,
                MimeType = a.MimeType,
                NombreArchivo = a.NombreArchivo,
            });
        }

        return respuesta;
    }

    /// <summary>Huella de la definición congelada; el snapshot la trae, y si no se lee queda vacía (core-api la trata como desconocida).</summary>
    private static string DefinicionesHuella(SolicitudTramite s) =>
        DefinicionesService.Leer(s.DefinicionSnapshot)?.Huella ?? string.Empty;

    private static Dictionary<string, string> LeerCampos(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpciones) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Error con <c>ErrorInfo.reason</c> estable y un mensaje fijo: nunca lleva datos de la solicitud.</summary>
    private static RpcException Error(StatusCode estado, string reason, string mensaje) =>
        PlatformRpcErrors.ToRpcException(reason, mensaje, estado);
}

/// <summary>Registro y mapeo del gRPC de lectura para radicar, con sus propios archivos para no tocar el resto de <c>Program</c>.</summary>
internal static class RadicacionGrpcExtensions
{
    /// <summary>Exige el token de servicio de core-api (<c>aud=personanatural</c>, scope <see cref="RadicacionGrpcService.Scope"/>).</summary>
    public static IGrpcServerBuilder AddRadicacionGrpc(this IGrpcServerBuilder builder) =>
        builder.RequireServiceToken<RadicacionGrpcService>(RadicacionGrpcService.Scope, ServicioSettings.Codigo);

    /// <summary>Mapea el servicio SOLO en el puerto interno del gRPC.</summary>
    public static IEndpointRouteBuilder MapRadicacionGrpc(this IEndpointRouteBuilder app, string grpcHost)
    {
        app.MapGrpcService<RadicacionGrpcService>().RequireHost(grpcHost);
        return app;
    }
}
