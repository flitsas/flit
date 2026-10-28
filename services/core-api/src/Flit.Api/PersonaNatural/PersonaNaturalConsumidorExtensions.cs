using Flit.Infrastructure.Persistence;
using Flit.PersonaNatural.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Platform.Sdk.Messaging;

namespace Flit.Api.PersonaNatural;

/// <summary>
/// Registro del consumidor del portal de persona natural (HU #14050 y #14051, Épica #14021). APAGADO por defecto: sin
/// <see cref="ClaveHabilitado"/> no se registra NADA (ni colas, ni cliente gRPC, ni publicador), así que el comportamiento de
/// core-api es exactamente el de antes. Encendido exige el bus de Trámites y la dirección del servicio del portal.
/// </summary>
internal static class PersonaNaturalConsumidorExtensions
{
    public const string ClaveHabilitado = "PersonaNatural:Consumidor:Habilitado";
    public const string ClaveDireccion = "PersonaNatural:Portal:Address";
    public const string ClaveDeadline = "PersonaNatural:Portal:DeadlineSegundos";

    /// <summary>Scope del token de servicio de <c>svc-tramites</c> para leer solicitudes del portal (audiencia <c>personanatural</c>).</summary>
    public const string ScopeLectura = "platform.personanatural.leer";

    public static IServiceCollection AddPersonaNaturalConsumidor(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue(ClaveHabilitado, false))
            return services;

        if (!configuration.GetValue("Tramites:Bus:Habilitado", false))
            throw new InvalidOperationException($"{ClaveHabilitado}=true exige Tramites:Bus:Habilitado=true: la solicitud llega y el resultado vuelve por el bus.");
        if (!Uri.TryCreate(configuration[ClaveDireccion], UriKind.Absolute, out var direccion))
            throw new InvalidOperationException($"{ClaveDireccion} no es una URL (p. ej. http://core-personanatural:8090): core-api lee la solicitud radicada por pull.");

        var deadline = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue(ClaveDeadline, 15)));
        services.AddFlitGrpcClient<RadicacionService.RadicacionServiceClient>(configuration, direccion, ScopeLectura, o => o.Deadline = deadline);
        services.AddScoped<IPersonaNaturalPortalClient, GrpcPersonaNaturalPortalClient>();
        services.AddScoped<IPersonaNaturalRadicador, PersonaNaturalRadicacionHandler>();
        services.AddFlitConsumer<FlitDbContext, RadicacionSolicitadaConsumer, RadicacionSolicitadaDatos>(
            configuration, RadicacionSolicitadaConsumer.Cola, PersonaNaturalEventos.Productor, [PersonaNaturalEventos.RadicacionSolicitada]);

        // HU #14051: espejo de estado de vuelta (cambios de estado y apertura de subsanación de los trámites del portal).
        return services.AddPersonaNaturalEstadoDeVuelta(configuration);
    }
}
