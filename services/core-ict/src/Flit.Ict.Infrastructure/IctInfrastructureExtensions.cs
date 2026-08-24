using Flit.Ict.Application.Register;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Trazabilidad;
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
        services.AddScoped<IIctStatusV2Query, IctStatusV2Query>();
        services.AddScoped<ISecretariesV1Query, SecretariesV1Query>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IAttachmentDocTypeResolver, AttachmentDocTypeResolver>();
        // Typed HttpClient del File Manager: BaseAddress al File Manager (las subidas/descargas a S3 usan
        // la presigned URL absoluta, que lo ignora). BaseUrl OBLIGATORIA — mismo criterio que core-api:
        // sin File Manager NO se sintetiza un path falso (eso dejaba adjuntos que no abrían).
        services.AddHttpClient<IIctAttachmentStorage, Storage.FileManagerAttachmentStorage>((sp, c) =>
            {
                var o = sp.GetRequiredService<IOptions<Storage.FileManagerOptions>>().Value;
                if (string.IsNullOrWhiteSpace(o.BaseUrl))
                {
                    throw new InvalidOperationException(
                        "FileManager:BaseUrl (o FILE_MANAGER_BASE_URL) es obligatoria para los adjuntos de ICT.");
                }

                var baseUrl = o.BaseUrl.EndsWith('/') ? o.BaseUrl : o.BaseUrl + "/";
                c.BaseAddress = new Uri(baseUrl);
                c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
            })
            .AddHttpMessageHandler(sp => new Logging.IctOutboundLoggingHandler(
                sp.GetRequiredService<IServiceScopeFactory>(), "external"));

        // Observabilidad (HU5): logs en Postgres (escritura + consulta enmascarada) y métricas de alerta.
        services.AddScoped<IntegrationLogRepository>();
        services.AddScoped<IIntegrationLogWriter>(sp => sp.GetRequiredService<IntegrationLogRepository>());
        services.AddScoped<IIntegrationLogQuery>(sp => sp.GetRequiredService<IntegrationLogRepository>());
        services.AddScoped<IIctAlertMetricsQuery, IctAlertMetricsQuery>();

        // Trazabilidad ICT por trámite (Feature #11814). Solo lectura.
        services.AddScoped<ITrazabilidadBandejaQuery, DbTrazabilidadBandejaRepository>();
        services.AddScoped<IRecorridoTramiteQuery, DbRecorridoTramiteRepository>();
        services.AddScoped<IConsultasFuenteQuery, DbConsultasFuenteRepository>();
        services.AddScoped<DbDetalleTramiteRepository>();
        services.AddScoped<IDatosTramiteQuery>(sp => sp.GetRequiredService<DbDetalleTramiteRepository>());
        services.AddScoped<ILogTramiteQuery>(sp => sp.GetRequiredService<DbDetalleTramiteRepository>());

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
        // Parámetros de cadencia/concurrencia/lote configurables en BD (ict.job_settings), leídos en
        // caliente por los jobs; fallback a IctJobOptions. Singleton compartido por los 5 jobs.
        services.AddSingleton<Jobs.IIctJobSettingsProvider, Jobs.IctJobSettingsProvider>();
        services.AddHttpClient("ict-webhook", client => client.Timeout = TimeSpan.FromSeconds(30))
            .AddHttpMessageHandler(sp => new Logging.IctOutboundLoggingHandler(
                sp.GetRequiredService<IServiceScopeFactory>(), "webhook"));
        services.AddHostedService<BusinessValidationJob>();
        services.AddHostedService<ExternalValidationJob>();
        services.AddHostedService<OrchestratorJob>();
        services.AddHostedService<SendToCoreApiJob>();
        services.AddHostedService<WebhookNotificationJob>();
        // Purga de observabilidad (logs/eventos/job_runs) — corre 24/7, fuera de la ventana del pipeline.
        services.AddHostedService<RetentionJob>();

        // Service-token gRPC este-oeste (core-ict → core-api): JWT de sistema que autentica que la
        // llamada la hace core-ict (no un tercero que alcance el puerto interno). Secreto compartido (HMAC).
        services.Configure<Security.IctServiceTokenOptions>(
            configuration.GetSection(Security.IctServiceTokenOptions.SectionName));
        services.AddSingleton<Security.IctServiceTokenProvider>();
        services.AddSingleton<Security.IctServiceTokenClientInterceptor>();

        // Cliente gRPC hacia core-api (orquestación + consultas). Cada llamada adjunta el service-token.
        var grpcAddress = configuration["CoreApiGrpc:Address"];
        if (!string.IsNullOrWhiteSpace(grpcAddress))
        {
            var grpcUri = new Uri(grpcAddress);
            services.AddGrpcClient<IctOrchestration.IctOrchestrationClient>(options =>
                    options.Address = grpcUri)
                .AddInterceptor<Security.IctServiceTokenClientInterceptor>();
            services.AddScoped<IProcedureDraftClient, IctGrpcProcedureDraftClient>();

            // Consulta real de fuentes externas: se delega en core-api (reusa RUNT/SOAT/RTM/RNMC).
            services.AddGrpcClient<IctConsultation.IctConsultationClient>(options =>
                    options.Address = grpcUri)
                .AddInterceptor<Security.IctServiceTokenClientInterceptor>();
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
