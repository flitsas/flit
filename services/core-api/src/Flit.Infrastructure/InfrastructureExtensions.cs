using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Storage;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Flit.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddPostgresInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<FlitDbContext>((serviceProvider, opts) =>
            opts.UseNpgsql(
                connectionString,
                npgsql =>
                {
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                })
            .UseSnakeCaseNamingConvention()
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false));

        services.AddScoped<IProcedureTypeRepository, ProcedureTypeRepository>();
        services.AddScoped<IProcedureInstanceRepository, ProcedureInstanceRepository>();
        services.AddScoped<ICatalogRepository, CatalogRepository>();

        AddAttachmentStorage(services, configuration, environment);
        AddConsultationProviders(services);

        return services;
    }

    private static void AddAttachmentStorage(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Raíz configurable: ATTACHMENTS_ROOT (env) > sección Attachments:Root (config) >
        // default {ContentRoot}/uploads/tramites. Relativa ⇒ se ancla al content root.
        var configured = Environment.GetEnvironmentVariable("ATTACHMENTS_ROOT")
            ?? configuration[$"{AttachmentStorageOptions.SectionName}:Root"];

        string root;
        if (string.IsNullOrWhiteSpace(configured))
            root = Path.Combine(environment.ContentRootPath, "uploads", "tramites");
        else if (Path.IsPathRooted(configured))
            root = configured;
        else
            root = Path.Combine(environment.ContentRootPath, configured);

        services.AddSingleton<IAttachmentStorage>(_ => new DiskAttachmentStorage(root));
    }

    private static void AddConsultationProviders(IServiceCollection services)
    {
        // Modos real|mock por proveedor (VERIFIK_VEHICLE_MODE, VERIFIK_SIMIT_MODE, etc.)
        services.Configure<ConsultationProviderModeOptions>(o =>
        {
            o.VerifikVehicleMode = Environment.GetEnvironmentVariable("VERIFIK_VEHICLE_MODE") ?? "real";
            o.VerifikSimitMode = Environment.GetEnvironmentVariable("VERIFIK_SIMIT_MODE") ?? "mock";
            o.VerifikRnmcMode = Environment.GetEnvironmentVariable("VERIFIK_RNMC_MODE") ?? "mock";
            o.VerifikConductorMode = Environment.GetEnvironmentVariable("VERIFIK_CONDUCTOR_MODE") ?? "mock";
            o.IntempoMode = Environment.GetEnvironmentVariable("INTEMPO_MODE") ?? "mock";
        });

        // Config Verifik desde variables de entorno VERIFIK_* (fuente de verdad:
        // .env.verifik.example). En prod se cargan via docker-compose / shell.
        services.Configure<VerifikOptions>(o =>
        {
            o.BaseUrl = Environment.GetEnvironmentVariable("VERIFIK_BASE_URL") ?? "https://api.verifik.co";
            o.ApiToken = Environment.GetEnvironmentVariable("VERIFIK_API_TOKEN") ?? "";
            o.AuthScheme = Environment.GetEnvironmentVariable("VERIFIK_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("VERIFIK_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // Config INTEMPO
        services.Configure<IntempoOptions>(o =>
        {
            o.BaseUrl = Environment.GetEnvironmentVariable("INTEMPO_BASE_URL") ?? "https://www.moviliza.com.co";
            o.TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("INTEMPO_TIMEOUT_SECONDS"), out var t) ? t : 15;
        });

        // Typed HttpClients (compatibles con PublishAot).
        services.AddHttpClient<VerifikConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikSimitConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikRnmcConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<VerifikConductorConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        services.AddHttpClient<IntempoConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<IntempoOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Proveedores expuestos como IConsultationProvider para el registry.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikSimitConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikRnmcConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConductorConsultationProvider>());
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<IntempoConsultationProvider>());
        services.AddSingleton<IConsultationProvider, FlitIntegrationsGatewayProvider>();
        services.AddScoped<IConsultationProviderRegistry, ConsultationProviderRegistry>();
    }
}
