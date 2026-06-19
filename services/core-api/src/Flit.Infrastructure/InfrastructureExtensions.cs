using Flit.Infrastructure.Consultations;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
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

        AddConsultationProviders(services);

        return services;
    }

    private static void AddConsultationProviders(IServiceCollection services)
    {
        // Config Verifik desde variables de entorno VERIFIK_* (fuente de verdad:
        // .env.verifik.example). En prod se cargan via docker-compose / shell.
        services.Configure<VerifikOptions>(o =>
        {
            o.BaseUrl = Environment.GetEnvironmentVariable("VERIFIK_BASE_URL") ?? "https://api.verifik.co";
            o.ApiToken = Environment.GetEnvironmentVariable("VERIFIK_API_TOKEN") ?? "";
            o.AuthScheme = Environment.GetEnvironmentVariable("VERIFIK_AUTH_SCHEME") ?? "Bearer";
            o.TimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("VERIFIK_TIMEOUT_SECONDS"), out var t) ? t : 30;
        });

        // Typed HttpClient para Verifik (compatible con PublishAot).
        services.AddHttpClient<VerifikConsultationProvider>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<VerifikOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        });

        // Proveedores expuestos como IConsultationProvider para el registry.
        services.AddTransient<IConsultationProvider>(sp => sp.GetRequiredService<VerifikConsultationProvider>());
        services.AddSingleton<IConsultationProvider, FlitIntegrationsGatewayProvider>();
        services.AddScoped<IConsultationProviderRegistry, ConsultationProviderRegistry>();
    }
}
