using Flit.Notificaciones.Grpc.V1;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Api.Notificaciones;

/// <summary>
/// core-api frente a core-notificaciones (Epic #13316, Feature #13324). Con <see cref="AddressKey"/> configurada,
/// registra el cliente gRPC de administración de mensajes muertos (HU #13357), que pide su token como svc-tramites con el
/// scope <c>platform.notificaciones.admin</c>. Sin ella, la consola responde que Notificaciones no está en el ambiente.
/// </summary>
internal static class NotificacionesRemoto
{
    public const string AddressKey = "Notificaciones:Remoto:Address";

    public static IServiceCollection AddNotificacionesRemoto(this IServiceCollection services, IConfiguration configuration)
    {
        var address = configuration[AddressKey];
        if (string.IsNullOrWhiteSpace(address))
            return services;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{AddressKey} no es una URL (p. ej. http://core-notificaciones:8085).");

        services.AddFlitGrpcClient<MensajesMuertosService.MensajesMuertosServiceClient>(configuration, uri, "platform.notificaciones.admin");
        return services;
    }
}
