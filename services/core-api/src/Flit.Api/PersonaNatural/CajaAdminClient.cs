using Flit.Api.Endpoints;
using System.Globalization;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.PersonaNatural;

/// <summary>Un cobro en caja tal como lo informa el portal (campos de <c>CobroResumen</c>, sin traducir).</summary>
internal sealed record CajaCobroResumen(
    string CobroId, string Codigo, long ValorCop, string Medio, string MedioDetalle, string PagadoEn, string PagadoPorNombre, string Estado,
    bool Anulable, string MotivoNoAnulable, string AnuladoEn, string AnuladoPorNombre, string AnulacionMotivo, long RowVersion, string AnulableHasta = "");

internal sealed record CajaCobrosLista(IReadOnlyList<CajaCobroResumen> Items, int Total, long TotalDiaCop);

/// <summary>El trámite que la caja va a cobrar (lo que el portal permite ver; el organismo lo completa core-api).</summary>
internal sealed record CajaTramite(
    string Codigo, string TipoCodigo, string TipoNombre, IReadOnlyList<CajaLinea> Lineas, long TotalCop, string VersionTarifa, DateTimeOffset VenceEn);

internal sealed record CajaLinea(string Concepto, long ValorCop);

/// <summary>Lo que core-api manda a confirmar: el usuario y el organismo salen del JWT, nunca del cliente.</summary>
internal sealed record CajaConfirmacion(
    Guid OrganismoId, string Codigo, Guid UsuarioId, string UsuarioNombre, string Medio, string Detalle, long? TotalVisto, string Llave,
    byte[]? Prefactura, string? PrefacturaTipo, string? PrefacturaNombre, string IpHash, int? VentanaAnulacionMinutos = null);

internal sealed record CajaCobroConfirmado(string Codigo, long ValorCop, string Medio, string? MedioDetalle, DateTimeOffset PagadoEn, DateTimeOffset? AnulableHasta = null);

/// <summary>
/// El portal rechazó la operación con un código estable (<c>no_encontrado</c>, <c>ya_pagado</c>, <c>total_cambio</c>, <c>demasiados_intentos</c>...).
/// <see cref="Extras"/> trae los datos de apoyo (<c>totalActualCop</c>, <c>retryAfterSegundos</c>).
/// </summary>
internal sealed class CajaPortalRechazoException(string code, string mensaje, int estadoHttp, IReadOnlyDictionary<string, string> extras) : Exception(mensaje)
{
    public string Code { get; } = code;

    public int EstadoHttp { get; } = estadoHttp;

    public IReadOnlyDictionary<string, string> Extras { get; } = extras;
}

/// <summary>Resultado de anular: <see cref="Razon"/> es <c>null</c> si salió bien, o la razón del portal (<c>cobro_no_anulable</c>, <c>cobro_no_encontrado</c>).</summary>
internal sealed record CajaAnulacionResultado(string? Razon, string Estado)
{
    public const string NoAnulable = "cobro_no_anulable";
    public const string NoEncontrado = "cobro_no_encontrado";
}

/// <summary>El portal no respondió (servicio caído, deadline o circuito abierto). El endpoint lo traduce a 503 <c>PORTAL_NO_DISPONIBLE</c>.</summary>
internal sealed class CajaPortalNoDisponibleException(Exception inner) : Exception("El portal de persona natural no está disponible.", inner);

/// <summary>
/// Puerto de la administración del cobro en caja (HU #14461, ADR-0082). El organismo lo resuelve core-api desde el token del
/// administrador y viaja en el mensaje; el portal no lo discute. Las pruebas lo reemplazan por un doble.
/// </summary>
internal interface ICajaAdminClient
{
    Task<CajaCobrosLista> ListarAsync(Guid organismoId, string? fecha, int pagina, int tamano, CancellationToken ct);

    Task<CajaAnulacionResultado> AnularAsync(Guid organismoId, Guid cobroId, string motivo, Guid actorUserId, string actorNombre, long rowVersion, CancellationToken ct);

    /// <summary>Busca el trámite por su ID en la caja del organismo. Lanza <see cref="CajaPortalRechazoException"/> si el portal lo rechaza.</summary>
    Task<CajaTramite> BuscarAsync(Guid organismoId, string codigo, Guid usuarioId, string ipHash, CancellationToken ct);

    /// <summary>Confirma el cobro. Lanza <see cref="CajaPortalRechazoException"/> si el portal lo rechaza.</summary>
    Task<CajaCobroConfirmado> ConfirmarAsync(CajaConfirmacion pedido, CancellationToken ct);
}

/// <summary>
/// Cliente gRPC de <c>flit.personanatural.v1.CajaAdminService</c> con el token de servicio de <c>svc-tramites</c> (audiencia
/// <c>personanatural</c>, scope <c>platform.personanatural.caja</c>). La empresa de la llamada es la de Persona Natural, dueña del cobro.
/// </summary>
internal sealed class GrpcCajaAdminClient(CajaAdminService.CajaAdminServiceClient client) : ICajaAdminClient
{
    private static Metadata Empresa() => new() { { PlatformServiceCallInterceptor.TenantMetadata, PlatformTenants.PersonaNatural.ToString() } };

    public async Task<CajaCobrosLista> ListarAsync(Guid organismoId, string? fecha, int pagina, int tamano, CancellationToken ct)
    {
        try
        {
            var r = await client.ListarCobrosAsync(
                new ListarCobrosRequest { OrganismoId = organismoId.ToString(), Fecha = fecha ?? string.Empty, Pagina = pagina, Tamano = tamano },
                Empresa(), cancellationToken: ct).ConfigureAwait(false);
            return new CajaCobrosLista([.. r.Items.Select(Mapear)], r.Total, r.TotalDiaCop);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal or StatusCode.Unknown)
        {
            throw new CajaPortalNoDisponibleException(ex);
        }
    }

    public async Task<CajaAnulacionResultado> AnularAsync(
        Guid organismoId, Guid cobroId, string motivo, Guid actorUserId, string actorNombre, long rowVersion, CancellationToken ct)
    {
        try
        {
            var r = await client.AnularCobroAsync(
                new AnularCobroRequest
                {
                    OrganismoId = organismoId.ToString(),
                    CobroId = cobroId.ToString(),
                    Motivo = motivo,
                    ActorUserId = actorUserId.ToString(),
                    ActorNombre = actorNombre,
                    RowVersion = rowVersion,
                },
                Empresa(), cancellationToken: ct).ConfigureAwait(false);
            return new CajaAnulacionResultado(null, r.Estado);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition && PlatformRpcErrors.Reason(ex) == CajaAnulacionResultado.NoAnulable)
        {
            return new CajaAnulacionResultado(CajaAnulacionResultado.NoAnulable, string.Empty);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound && PlatformRpcErrors.Reason(ex) == CajaAnulacionResultado.NoEncontrado)
        {
            return new CajaAnulacionResultado(CajaAnulacionResultado.NoEncontrado, string.Empty);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal or StatusCode.Unknown)
        {
            throw new CajaPortalNoDisponibleException(ex);
        }
    }

    public async Task<CajaTramite> BuscarAsync(Guid organismoId, string codigo, Guid usuarioId, string ipHash, CancellationToken ct)
    {
        try
        {
            var r = await client.BuscarTramiteAsync(
                new BuscarTramiteRequest { OrganismoId = organismoId.ToString(), Codigo = codigo, UsuarioId = usuarioId.ToString(), IpHash = ipHash },
                Empresa(), cancellationToken: ct).ConfigureAwait(false);
            return new CajaTramite(
                r.Codigo, r.TipoTramiteCodigo, r.TipoTramiteNombre, [.. r.Lineas.Select(l => new CajaLinea(l.Concepto, l.ValorCop))], r.TotalCop, r.VersionTarifa,
                DateTimeOffset.Parse(r.VenceEn, CultureInfo.InvariantCulture));
        }
        catch (RpcException ex)
        {
            throw Traducir(ex);
        }
    }

    public async Task<CajaCobroConfirmado> ConfirmarAsync(CajaConfirmacion pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var request = new ConfirmarCobroRequest
        {
            OrganismoId = pedido.OrganismoId.ToString(),
            Codigo = pedido.Codigo,
            UsuarioId = pedido.UsuarioId.ToString(),
            UsuarioNombre = pedido.UsuarioNombre,
            MedioCaja = pedido.Medio,
            Detalle = pedido.Detalle,
            IdempotencyKey = pedido.Llave,
            IpHash = pedido.IpHash,
        };
        if (pedido.VentanaAnulacionMinutos is { } ventana)
            request.VentanaAnulacionMinutos = ventana;
        if (pedido.TotalVisto is { } visto)
            request.TotalVisto = visto;
        if (pedido.Prefactura is { Length: > 0 } archivo)
        {
            request.Prefactura = Google.Protobuf.ByteString.CopyFrom(archivo);
            request.PrefacturaContentType = pedido.PrefacturaTipo ?? string.Empty;
            request.PrefacturaNombre = pedido.PrefacturaNombre ?? string.Empty;
        }

        try
        {
            var r = await client.ConfirmarCobroAsync(request, Empresa(), cancellationToken: ct).ConfigureAwait(false);
            return new CajaCobroConfirmado(
                r.Codigo, r.ValorCop, r.Medio, r.MedioDetalle.Length == 0 ? null : r.MedioDetalle, DateTimeOffset.Parse(r.PagadoEn, CultureInfo.InvariantCulture),
                r.AnulableHasta.Length == 0 ? null : DateTimeOffset.Parse(r.AnulableHasta, CultureInfo.InvariantCulture));
        }
        catch (RpcException ex)
        {
            throw Traducir(ex);
        }
    }

    /// <summary>
    /// Un rechazo del portal con código estable (<c>ErrorInfo.reason</c>) es una respuesta para la caja; cualquier otro fallo (servicio
    /// caído, deadline, permisos del token de servicio) es «portal no disponible».
    /// </summary>
    private static Exception Traducir(RpcException ex)
    {
        var reason = PlatformRpcErrors.Reason(ex);
        var http = ex.StatusCode switch
        {
            StatusCode.InvalidArgument => 400,
            StatusCode.PermissionDenied => 403,
            StatusCode.NotFound => 404,
            StatusCode.FailedPrecondition => 409,
            StatusCode.ResourceExhausted => 429,
            StatusCode.Unavailable when reason == "almacenamiento_no_disponible" => 502,
            _ => 0,
        };
        if (reason is null || http == 0)
            return new CajaPortalNoDisponibleException(ex);

        var extras = ex.GetRpcStatus()?.GetDetail<Google.Rpc.ErrorInfo>()?.Metadata.ToDictionary(m => m.Key, m => m.Value) ?? [];
        return new CajaPortalRechazoException(reason, ex.Status.Detail, http, extras);
    }

    private static CajaCobroResumen Mapear(CobroResumen c) => new(
        c.CobroId, c.Codigo, c.ValorCop, c.Medio, c.MedioDetalle, c.PagadoEn, c.PagadoPorNombre, c.Estado,
        c.Anulable, c.MotivoNoAnulable, c.AnuladoEn, c.AnuladoPorNombre, c.AnulacionMotivo, c.RowVersion, c.AnulableHasta);
}

/// <summary>
/// Registro de la administración del cobro en caja (HU #14461). APAGADO por defecto: sin <see cref="ClaveHabilitado"/> no se registra
/// NADA (ni el cliente gRPC), así que core-api es exactamente el de antes y los endpoints responden 404.
/// </summary>
internal static class CajaAdminExtensions
{
    public const string ClaveHabilitado = "PortalCaja:Habilitado";

    /// <summary>Scope del token de servicio de <c>svc-tramites</c> para administrar los cobros en caja (audiencia <c>personanatural</c>).</summary>
    public const string ScopeCaja = "platform.personanatural.caja";

    public static bool Habilitado(IConfiguration configuration) => configuration.GetValue(ClaveHabilitado, false);

    public const string ClaveVentanaMax = "PortalCaja:VentanaAnulacionMaxMinutos";
    public const string ClaveVentanaPorDefecto = "PortalCaja:VentanaAnulacionPorDefectoMinutos";

    /// <summary>
    /// Rango de la ventana de anulación por organismo (0..max, default 15). Se registra SIEMPRE: la configuración de Cobro del portal no
    /// depende de que la caja esté encendida. El tope nunca supera el de la base (1440).
    /// </summary>
    public static IServiceCollection AddCajaAnulacionOpciones(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var max = Math.Clamp(configuration.GetValue(ClaveVentanaMax, Flit.Admin.Domain.Plataforma.PersonaNatural.CajaAnulacionOpciones.Maximo), 0, Flit.Admin.Domain.Plataforma.PersonaNatural.CajaAnulacionOpciones.TopeDeLaBase);
        var porDefecto = Math.Clamp(configuration.GetValue(ClaveVentanaPorDefecto, Flit.Admin.Domain.Plataforma.PersonaNatural.CajaAnulacionOpciones.PorDefecto), 0, max);
        services.AddSingleton(new Flit.Admin.Domain.Plataforma.PersonaNatural.CajaAnulacionOpciones(porDefecto, max));
        return services;
    }

    public static IServiceCollection AddPortalCaja(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Habilitado(configuration))
            return services;

        if (!Uri.TryCreate(configuration[PersonaNaturalConsumidorExtensions.ClaveDireccion], UriKind.Absolute, out var direccion))
            throw new InvalidOperationException(
                $"{PersonaNaturalConsumidorExtensions.ClaveDireccion} no es una URL (p. ej. http://core-personanatural:8090): el hub lee y anula los cobros en caja en el portal.");

        var deadline = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue(PersonaNaturalConsumidorExtensions.ClaveDeadline, 15)));
        services.AddFlitGrpcClient<CajaAdminService.CajaAdminServiceClient>(configuration, direccion, ScopeCaja, o => o.Deadline = deadline);
        services.AddScoped<ICajaAdminClient, GrpcCajaAdminClient>();
        services.AddScoped<IActorCajaStore, EfActorCajaStore>(); // organismo y nombre del actor de la caja (/api/v1/ot/caja)
        return services;
    }
}
