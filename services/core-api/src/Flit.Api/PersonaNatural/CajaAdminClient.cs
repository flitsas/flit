using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;

namespace Flit.Api.PersonaNatural;

/// <summary>Un cobro en caja tal como lo informa el portal (campos de <c>CobroResumen</c>, sin traducir).</summary>
internal sealed record CajaCobroResumen(
    string CobroId, string Codigo, long ValorCop, string Medio, string MedioDetalle, string PagadoEn, string PagadoPorNombre, string Estado,
    bool Anulable, string MotivoNoAnulable, string AnuladoEn, string AnuladoPorNombre, string AnulacionMotivo, long RowVersion);

internal sealed record CajaCobrosLista(IReadOnlyList<CajaCobroResumen> Items, int Total, long TotalDiaCop);

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

    private static CajaCobroResumen Mapear(CobroResumen c) => new(
        c.CobroId, c.Codigo, c.ValorCop, c.Medio, c.MedioDetalle, c.PagadoEn, c.PagadoPorNombre, c.Estado,
        c.Anulable, c.MotivoNoAnulable, c.AnuladoEn, c.AnuladoPorNombre, c.AnulacionMotivo, c.RowVersion);
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
        return services;
    }
}
