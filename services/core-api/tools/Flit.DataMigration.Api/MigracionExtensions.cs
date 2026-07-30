using Flit.DataMigration.Api.Authorization;
using Flit.DataMigration.Api.Logging;
using Flit.DataMigration.V1.Configuration;
using Flit.DataMigration.V1.Loading;
using Flit.DataMigration.V1.Orchestration;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.Api;

/// <summary>Registro del motor de migración en el host HTTP.</summary>
public static class MigracionExtensions
{
    public static IServiceCollection AddMigracionEngine(
        this IServiceCollection services, IConfiguration configuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = MigrationSettings.Bind(configuration);

        // Crear un tenant desde una petición HTTP es categóricamente más peligroso que desde una
        // consola a la que alguien tuvo que entrar por SSH: tenant_id es NOT NULL pero SIN foreign
        // key, así que un id inventado no lo frena la base y RLS escondería los trámites de su
        // dueño real. Se fuerza a false pase lo que pase en la configuración.
        if (settings.CreateTenantIfMissing)
        {
            MigracionLog.CreacionDeTenantsDesactivada(logger);
            settings = settings with { CreateTenantIfMissing = false };
        }

        services.AddSingleton(settings);

        // El MISMO registro que hace el migrador de consola: UseNpgsql + snake_case, y nada más.
        //
        // NO se usa AddPostgresInfrastructure a propósito. Registra
        // EnableRetryOnFailure(3, 5s) (InfrastructureExtensions.cs) y ProcedureInstanceLoader abre
        // transacciones a mano con BeginTransactionAsync, que la estrategia reintentante RECHAZA
        // ("does not support user-initiated transactions"). Además rompería el advisory lock de
        // MigrationLock, que necesita quedarse en la MISMA conexión toda la petición.
        //
        // NO añadir EnableRetryOnFailure aquí.
        services.AddDbContext<FlitDbContext>(o => o
            .UseNpgsql(settings.V2Connection)
            .UseSnakeCaseNamingConvention());

        services.AddMigrationHttpClients(settings);

        // Scoped = una petición, un trámite, una unidad de trabajo. FlitDbContext no es thread-safe,
        // así que un singleton corrompería el change tracker con dos peticiones a la vez.
        services.AddScoped<MigrationMapStore>();
        services.AddScoped<AttachmentMapStore>();
        services.AddScoped<MigrationLock>();
        services.AddScoped<MigrationRunner>();

        // TenantResolver NO se registra: lo construye MigrationRunner dentro de cada ejecución,
        // igual que la consola. Es una foto completa de identity.tenants que además se MUTA a
        // mitad de lote (Register) sin candado alguno; como singleton se quedaría rancio y los
        // trámites caerían bajo el tenant equivocado.

        services.AddSingleton<MigracionApiKey>();
        services.AddSingleton<MigracionConcurrencyFilter>();
        services.AddScoped<MigracionKeyFilter>();
        services.AddHostedService<MigracionSchemaInitializer>();

        return services;
    }
}
