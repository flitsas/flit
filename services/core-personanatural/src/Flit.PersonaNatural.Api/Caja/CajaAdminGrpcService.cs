using System.Globalization;
using Flit.PersonaNatural.Api.Pagos;
using Flit.PersonaNatural.Api.Solicitudes;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Caja;

/// <summary>
/// <c>flit.personanatural.v1.CajaAdminService</c> (HU #14461): lista y anula los cobros en caja de UN organismo y, desde que la caja es un módulo de
/// FLIT 2.0 (core-api <c>/api/v1/ot/caja</c>), busca el trámite y confirma el cobro.
/// Solo el cliente <see cref="ClienteAutorizado"/> con aud=personanatural y scope <see cref="Scope"/> (lo valida el interceptor del
/// SDK; el cliente, este servicio). El organismo lo resuelve core-api desde el token del administrador y el BFF no lo discute: solo
/// verifica que el cobro sea de ESE organismo. Un token de caja de usuario nunca llega aquí (el interceptor exige un token de servicio).
/// </summary>
internal sealed class CajaAdminGrpcService(
    CajaListaService lista, CajaAnulacionService anulacion, CajaBusquedaService busqueda, CajaConfirmacionService confirmacion, TimeProvider reloj)
    : CajaAdminService.CajaAdminServiceBase
{
    public const string Scope = "platform.personanatural.caja";
    public const string ClienteAutorizado = "svc-tramites";
    public const string ReasonNoAnulable = "cobro_no_anulable";
    public const string ReasonNoEncontrado = "cobro_no_encontrado";

    public override async Task<ListarCobrosResponse> ListarCobros(ListarCobrosRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Autorizar(context);
        var organismo = Organismo(request.OrganismoId);
        DateOnly fecha;
        if (string.IsNullOrWhiteSpace(request.Fecha))
            fecha = VigenciaDelCobro.DiaColombia(reloj.GetUtcNow());
        else if (!DateOnly.TryParseExact(request.Fecha.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "fecha debe ser AAAA-MM-DD."));

        var (pagina, tamano) = CajaListaService.Paginar(request.Pagina, request.Tamano);
        var r = await lista.ListarAsync(organismo, fecha, pagina, tamano, context.CancellationToken).ConfigureAwait(false);
        var respuesta = new ListarCobrosResponse { Total = r.Total, TotalDiaCop = r.TotalDiaCop };
        foreach (var c in r.Items)
        {
            respuesta.Items.Add(new CobroResumen
            {
                CobroId = c.CobroId.ToString(),
                Codigo = c.Codigo,
                ValorCop = c.ValorCop,
                Medio = c.MedioCaja ?? string.Empty,
                MedioDetalle = c.MedioDetalle ?? string.Empty,
                PagadoEn = c.PagadoEn.ToString("O", CultureInfo.InvariantCulture),
                PagadoPorNombre = c.PagadoPorNombre ?? string.Empty,
                Estado = c.Estado,
                Anulable = c.Anulable,
                MotivoNoAnulable = c.MotivoNoAnulable ?? string.Empty,
                AnuladoEn = c.AnuladoEn?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                AnuladoPorNombre = c.AnuladoPorNombre ?? string.Empty,
                AnulacionMotivo = c.AnulacionMotivo ?? string.Empty,
                RowVersion = c.RowVersion,
                AnulableHasta = c.AnulableHasta?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            });
        }

        return respuesta;
    }

    public override async Task<AnularCobroResponse> AnularCobro(AnularCobroRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Autorizar(context);
        var organismo = Organismo(request.OrganismoId);
        if (!Guid.TryParse(request.CobroId, out var cobro) || cobro == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "cobro_id debe ser un UUID."));
        if (!Guid.TryParse(request.ActorUserId, out var actor) || actor == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "actor_user_id debe ser un UUID."));

        try
        {
            var anulado = await anulacion.AnularAsync(organismo, cobro, request.Motivo, actor, request.ActorNombre, request.RowVersion, context.CancellationToken).ConfigureAwait(false);
            return new AnularCobroResponse { CobroId = anulado.Id.ToString(), Estado = anulado.Estado };
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo == CajaAnulacionService.CodigoNoEncontrado)
        {
            throw PlatformRpcErrors.ToRpcException(ReasonNoEncontrado, ex.Message, StatusCode.NotFound);
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo == CajaAnulacionService.CodigoNoAnulable)
        {
            // El mensaje (en español, sin datos del trámite) es lo que el hub le muestra al administrador.
            throw PlatformRpcErrors.ToRpcException(ReasonNoAnulable, ex.Message, StatusCode.FailedPrecondition);
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo is CajaAnulacionService.CodigoMotivoInvalido or CajaAnulacionService.CodigoVersionRequerida or "actor_invalido")
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }

    /// <summary>Busca el trámite por su ID en la caja del organismo (HU #14456). Cuenta contra el tope de búsquedas sin resultado por usuario e IP.</summary>
    public override async Task<BuscarTramiteResponse> BuscarTramite(BuscarTramiteRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Autorizar(context);
        var cajero = ArmarCajero(request.OrganismoId, request.UsuarioId, string.Empty);
        try
        {
            var t = await busqueda.BuscarAsync(cajero, request.Codigo, IpHash(request.IpHash), context.CancellationToken).ConfigureAwait(false);
            var respuesta = new BuscarTramiteResponse
            {
                Codigo = t.Codigo,
                TipoTramiteCodigo = t.TipoTramite.Codigo,
                TipoTramiteNombre = t.TipoTramite.Nombre,
                TotalCop = t.TotalCop,
                VersionTarifa = t.VersionTarifa,
                VenceEn = t.VenceEn.ToString("O", CultureInfo.InvariantCulture),
            };
            foreach (var l in t.Lineas)
                respuesta.Lineas.Add(new LineaCobroCaja { Concepto = l.Concepto, ValorCop = l.ValorCop });
            return respuesta;
        }
        catch (ReglaDeNegocioException ex)
        {
            throw CajaErrores.ARpc(ex);
        }
    }

    /// <summary>
    /// Confirma el cobro en caja (HU #14457): medio obligatorio, prefactura opcional validada por firma, idempotencia por llave y usuario,
    /// y el primero que llega gana (el otro recibe <c>ya_pagado</c>).
    /// </summary>
    public override async Task<ConfirmarCobroResponse> ConfirmarCobro(ConfirmarCobroRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        Autorizar(context);
        var cajero = ArmarCajero(request.OrganismoId, request.UsuarioId, request.UsuarioNombre);
        var prefactura = request.Prefactura.Length == 0 ? null : request.Prefactura.ToByteArray();
        var pedido = new PedidoDeConfirmacion(
            request.Codigo, request.MedioCaja, request.Detalle, request.HasTotalVisto ? request.TotalVisto : null, request.IdempotencyKey,
            string.IsNullOrWhiteSpace(request.PrefacturaNombre) ? null : request.PrefacturaNombre, prefactura,
            request.HasVentanaAnulacionMinutos ? request.VentanaAnulacionMinutos : null);
        try
        {
            var r = await confirmacion.ConfirmarAsync(cajero, pedido, IpHash(request.IpHash), context.CancellationToken).ConfigureAwait(false);
            return new ConfirmarCobroResponse
            {
                Codigo = r.Codigo,
                ValorCop = r.ValorCop,
                Medio = r.Medio,
                MedioDetalle = r.MedioDetalle ?? string.Empty,
                PagadoEn = r.PagadoEn.ToString("O", CultureInfo.InvariantCulture),
                AnulableHasta = r.AnulableHasta?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            };
        }
        catch (ReglaDeNegocioException ex)
        {
            throw CajaErrores.ARpc(ex);
        }
    }

    /// <summary>El organismo (cuyo código y nombre solo los usa core-api) y el cajero que dice core-api, nunca el cliente final.</summary>
    private static Cajero ArmarCajero(string organismo, string usuario, string nombre)
    {
        if (!Guid.TryParse(usuario, out var usuarioId) || usuarioId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "usuario_id debe ser un UUID."));
        return new Cajero(usuarioId, nombre ?? string.Empty, Organismo(organismo), string.Empty, string.Empty);
    }

    /// <summary>La huella que manda core-api (SHA-256 de la IP) o <c>null</c>; el tope la firma de nuevo con la llave del BFF.</summary>
    private static string? IpHash(string valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static void Autorizar(ServerCallContext context)
    {
        var llamador = PlatformServiceCaller.From(context);
        if (!string.Equals(llamador.ClientId, ClienteAutorizado, StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Este cliente no puede administrar cobros de caja."));
    }

    private static Guid Organismo(string valor) =>
        Guid.TryParse(valor, out var id) && id != Guid.Empty ? id : throw new RpcException(new Status(StatusCode.InvalidArgument, "organismo_id debe ser un UUID."));
}

internal static class CajaAdminGrpcExtensions
{
    /// <summary>Exige el token de servicio de core-api (<c>aud=personanatural</c>, scope <see cref="CajaAdminGrpcService.Scope"/>).</summary>
    public static IGrpcServerBuilder AddCajaAdminGrpc(this IGrpcServerBuilder builder) =>
        builder.RequireServiceToken<CajaAdminGrpcService>(CajaAdminGrpcService.Scope, ServicioSettings.Codigo)
            // La prefactura (hasta 5 MB) viaja en ConfirmarCobro: el tope por defecto de gRPC (4 MB) no alcanza.
            .AddServiceOptions<CajaAdminGrpcService>(o => o.MaxReceiveMessageSize = MensajeMaxBytes);

    /// <summary>Prefactura máxima (5 MB) más el resto del mensaje.</summary>
    public const int MensajeMaxBytes = 6 * 1024 * 1024;

    /// <summary>Mapea el servicio SOLO en el puerto interno del gRPC y solo con la caja encendida.</summary>
    public static IEndpointRouteBuilder MapCajaAdminGrpc(this IEndpointRouteBuilder app, string grpcHost)
    {
        if (app.ServiceProvider.GetRequiredService<IOptions<CajaOptions>>().Value.Habilitado)
            app.MapGrpcService<CajaAdminGrpcService>().RequireHost(grpcHost);
        return app;
    }
}
