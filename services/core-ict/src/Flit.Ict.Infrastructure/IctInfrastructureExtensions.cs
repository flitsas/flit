using Flit.Ict.Application.Register;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Grpc.Contracts;
using Flit.Ict.Infrastructure.ExternalClients;
using Flit.Ict.Infrastructure.Jobs;
using Flit.Ict.Infrastructure.Logging;
using Flit.Ict.Infrastructure.Persistence;
using Flit.Ict.Infrastructure.Persistence.Repositories;
using Flit.Ict.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flit.Ict.Infrastructure;

/// <summary>Composición de la capa Infrastructure de core-ict (persistencia, seguridad, gRPC, jobs).</summary>
public static class IctInfrastructureExtensions
{
    public static IServiceCollection AddIctInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Core")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'ConnectionStrings:Core'.");

        services.AddDbContext<IctDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3))
                   .UseSnakeCaseNamingConvention());

        services.Configure<IctDatabaseOptions>(configuration.GetSection(IctDatabaseOptions.SectionName));
        services.Configure<IctJwtSettings>(configuration.GetSection(IctJwtSettings.SectionName));
        services.Configure<IctIngestOptions>(configuration.GetSection(IctIngestOptions.SectionName));

        services.Configure<Storage.FileManagerOptions>(configuration.GetSection(Storage.FileManagerOptions.SectionName));

        // Tenant/compañía del token ICT (impone RLS; el cliente nunca elige tenant).
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, HttpCurrentTenant>();
        services.AddScoped<IPreTramiteRepository, PreTramiteRepository>();
        services.AddScoped<IStatusProcessV1Query, StatusProcessV1Query>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddHttpClient<IIctAttachmentStorage, Storage.FileManagerAttachmentStorage>();

        // Observabilidad (HU5): logs en Postgres (escritura + consulta enmascarada) y métricas de alerta.
        services.AddScoped<IntegrationLogRepository>();
        services.AddScoped<IIntegrationLogWriter>(sp => sp.GetRequiredService<IntegrationLogRepository>());
        services.AddScoped<IIntegrationLogQuery>(sp => sp.GetRequiredService<IntegrationLogRepository>());
        services.AddScoped<IIctAlertMetricsQuery, IctAlertMetricsQuery>();

        // Seguridad (login ICT independiente).
        services.AddSingleton(sp => new IctJwtKeyMaterial(sp.GetRequiredService<IOptions<IctJwtSettings>>().Value));
        services.AddSingleton<IIctJwtTokenIssuer, IctRsaJwtTokenIssuer>();
        services.AddSingleton<IIctPasswordHasher, Argon2PasswordHasher>();

        // Repositorios.
        services.AddScoped<IIntegrationClientRepository, IntegrationClientRepository>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();

        // Bootstrap del schema ICT (DDL embebido idempotente al arrancar).
        services.AddHostedService<IctSchemaBootstrapper>();
        // Seed de desarrollo: cliente de integración de prueba para el login local (solo Development).
        services.AddHostedService<DevIntegrationClientSeeder>();
        // Datos mock para que el submódulo frontend (logs/alertas) muestre contenido (solo Development).
        services.AddHostedService<DevMockDataSeeder>();

        // Pipeline de validación: clientes externos + 5 jobs programados.
        services.Configure<IctJobOptions>(configuration.GetSection(IctJobOptions.SectionName));
        services.AddHttpClient("ict-webhook", client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddHostedService<BusinessValidationJob>();
        services.AddHostedService<ExternalValidationJob>();
        services.AddHostedService<OrchestratorJob>();
        services.AddHostedService<SendToCoreApiJob>();
        services.AddHostedService<WebhookNotificationJob>();

        // Cliente gRPC hacia core-api (orquestación de creación del borrador).
        // TODO(ICT-GRPC-AUTH): adjuntar el service-token (client-credentials) vía interceptor/CallCredentials.
        var grpcAddress = configuration["CoreApiGrpc:Address"];
        if (!string.IsNullOrWhiteSpace(grpcAddress))
        {
            var grpcUri = new Uri(grpcAddress);
            services.AddGrpcClient<IctOrchestration.IctOrchestrationClient>(options =>
                options.Address = grpcUri);
            services.AddScoped<IProcedureDraftClient, IctGrpcProcedureDraftClient>();

            // Consulta real de fuentes externas: se delega en core-api (reusa RUNT/SOAT/RTM/RNMC).
            services.AddGrpcClient<IctConsultation.IctConsultationClient>(options =>
                options.Address = grpcUri);
            services.AddScoped<IConsultationClient, IctGrpcConsultationClient>();
        }
        else
        {
            // Sin canal gRPC configurado: stubs que dejan el pre-trámite para el siguiente ciclo.
            services.AddScoped<IProcedureDraftClient, PendingProcedureDraftClient>();
            services.AddScoped<IConsultationClient, StubConsultationClient>();
        }

        return services;
    }
}
