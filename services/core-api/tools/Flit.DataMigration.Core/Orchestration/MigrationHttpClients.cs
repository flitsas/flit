using Flit.DataMigration.V1.Configuration;
using Flit.DataMigration.V1.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// Los tres clientes HTTP del migrador, registrados por nombre en <c>IHttpClientFactory</c>.
/// <para>
/// Van por fábrica y no por <c>new HttpClient()</c> porque uno de los hosts es un proceso largo:
/// crear un cliente por petición agota los sockets (quedan en TIME_WAIT) y además congela el DNS,
/// así que un cambio de IP del file-manager no se vería nunca. La consola también pasa por aquí
/// para que haya UN solo camino de construcción y los dos hosts no puedan diferir en timeouts.
/// </para>
/// </summary>
public static class MigrationHttpClients
{
    public const string SourceFileManager = "migracion.file-manager-origen";
    public const string TargetFileManager = "migracion.file-manager-destino";
    public const string V1Snapshot = "migracion.snapshot-v1";

    /// <summary>
    /// Los expedientes completos rondan los 9-12 MB y V1 los arma al vuelo: el timeout por
    /// defecto de 100 s se queda corto.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Con un timeout de 5 minutos, la rotación por defecto del handler (2 min) generaría
    /// reciclado inútil en mitad de una descarga.
    /// </summary>
    private static readonly TimeSpan HandlerLifetime = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddMigrationHttpClients(
        this IServiceCollection services, MigrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        AddFileManager(services, SourceFileManager, settings.SourceFileManager);
        AddFileManager(services, TargetFileManager, settings.TargetFileManager);

        services.AddHttpClient(V1Snapshot, http =>
        {
            if (settings.Snapshot.IsConfigured)
            {
                http.BaseAddress = Normalize(settings.Snapshot.BaseUrl);
            }

            http.Timeout = Timeout;
            // El consolidado de un expediente puede acercarse a los 100 MB en los trámites más
            // pesados; el límite por defecto (2 GB) no protege de nada y uno demasiado bajo
            // convierte un trámite grande en un fallo silencioso.
            http.MaxResponseContentBufferSize = 256L * 1024 * 1024;
        }).SetHandlerLifetime(HandlerLifetime);

        return services;
    }

    private static void AddFileManager(IServiceCollection services, string name, FileManagerEndpoint endpoint)
        => services.AddHttpClient(name, http =>
        {
            if (endpoint.IsConfigured)
            {
                http.BaseAddress = Normalize(endpoint.BaseUrl);
            }

            http.Timeout = Timeout;
        }).SetHandlerLifetime(HandlerLifetime);

    /// <summary>
    /// La barra final da igual en la configuración: <see cref="Uri"/> la exige para que las rutas
    /// relativas no se coman el último segmento del path.
    /// </summary>
    private static Uri Normalize(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
}
