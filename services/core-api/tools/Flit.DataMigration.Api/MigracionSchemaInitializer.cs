using Flit.DataMigration.V1.Configuration;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Mapping;
using Flit.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Flit.DataMigration.Api;

/// <summary>
/// Comprueba al arrancar que la base responde, que se puede crear el esquema <c>migration</c> y
/// que el entorno destino está listo para los dos tipos de trámite.
/// <para>
/// No es una optimización — las tablas se siguen creando en cada corrida, igual que en la consola,
/// para que los dos hosts se comporten igual. Es <b>fail-fast</b>: si la cadena de conexión está
/// mal o el rol no puede crear el esquema, el host debe negarse a arrancar en el despliegue, no
/// devolver 503 en la primera migración de una ola a las dos de la mañana.
/// </para>
/// <para>
/// De paso cierra una carrera: <c>TargetEnvironment.ResolveAsync</c> puede CREAR el usuario de
/// sistema, y dos peticiones simultáneas la primera vez chocarían contra el índice único de email.
/// Resolviéndolo aquí, cuando llegan las peticiones el usuario ya existe.
/// </para>
/// </summary>
internal sealed partial class MigracionSchemaInitializer(
    IServiceProvider services,
    IOptions<MigracionApiOptions> options,
    ILogger<MigracionSchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.RunSchemaCheckAtStartup)
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlitDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<MigrationSettings>();

        await new MigrationMapStore(db).EnsureCreatedAsync(cancellationToken);
        await new AttachmentMapStore(db).EnsureCreatedAsync(cancellationToken);

        foreach (var kind in V1ProcedureKind.All)
        {
            await TargetEnvironment.ResolveAsync(
                db, kind.ProcedureTypeCode, settings.SystemUserEmail, cancellationToken);
        }

        SchemaListo(logger, settings.SystemUserEmail);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Information,
        Message = "Esquema de migración listo; usuario de sistema {SystemUserEmail} resuelto para los dos tipos de trámite")]
    private static partial void SchemaListo(ILogger logger, string systemUserEmail);
}
