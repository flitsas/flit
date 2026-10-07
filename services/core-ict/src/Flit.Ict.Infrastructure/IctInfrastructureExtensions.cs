using Flit.Ict.Application.Register;
using Grpc.Core;
using Grpc.Core.Interceptors;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddScoped<ITiposTramiteQuery, DbTiposTramiteRepository>();
        services.AddScoped<IRecorridoTramiteQuery, DbRecorridoTramiteRepository>();
        services.AddScoped<IConsultasFuenteQuery, DbConsultasFuenteRepository>();
        services.AddScoped<DbDetalleTramiteRepository>();
        services.AddScoped<IDatosTramiteQuery>(sp => sp.GetRequiredService<DbDetalleTramiteRepository>());
        services.AddScoped<ILogTramiteQuery>(sp => sp.GetRequiredService<DbDetalleTramiteRepository>());
        services.AddScoped<IRevelarDatosPersonalesQuery, DbRevelarDatosPersonalesRepository>();

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
        var useIdentityToken = AddIdentityServiceToken(services, configuration);

        // Cliente gRPC hacia core-api (orquestación + consultas). Cada llamada adjunta el service-token.
        var grpcAddress = configuration["CoreApiGrpc:Address"];
        if (!string.IsNullOrWhiteSpace(grpcAddress))
        {
            var grpcUri = new Uri(grpcAddress);
            WithServiceToken(services.AddGrpcClient<IctOrchestration.IctOrchestrationClient>(options =>
                    options.Address = grpcUri), useIdentityToken);
            services.AddScoped<IProcedureDraftClient, IctGrpcProcedureDraftClient>();

            // Consulta real de fuentes externas: directo a core-consultas (HU #13348), sin pasar por core-api.
            AddConsultasRemoto(services, configuration, useIdentityToken);
        }
        else
        {
            // Sin canal gRPC configurado: stubs que dejan el pre-trámite para el siguiente ciclo.
            services.AddScoped<IProcedureDraftClient, PendingProcedureDraftClient>();
            services.AddScoped<IConsultationClient, StubConsultationClient>();
        }

        return services;
    }
    /// <summary>
    /// Epic #13316 (HU #13335): con <c>Ict:ServiceToken:UseIdentity</c> el token sale de Identidad. Sin endpoint o sin
    /// secreto no arranca: es preferible un error claro al desplegar que llamadas que fallan en silencio en cada ciclo.
    /// </summary>
    internal static bool AddIdentityServiceToken(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(Security.IctServiceTokenOptions.SectionName).Get<Security.IctServiceTokenOptions>()
            ?? new Security.IctServiceTokenOptions();
        if (!options.UseIdentity)
            return false;

        if (!Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                "Ict:ServiceToken:UseIdentity está encendida pero faltan Ict:ServiceToken:TokenEndpoint o Ict:ServiceToken:ClientSecret.");
        }

        services.AddHttpClient(Security.IdentityServiceTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<Security.IdentityServiceTokenProvider>();
        return true;
    }

    /// <summary>
    /// El token de Identidad se pide de forma asíncrona, así que va como credencial de llamada (no como interceptor). El
    /// canal interno es h2c sin TLS: gRPC solo manda credenciales por un canal así si se le permite explícitamente.
    /// </summary>
    private static void WithServiceToken(IHttpClientBuilder client, bool useIdentityToken)
    {
        if (!useIdentityToken)
        {
            client.AddInterceptor<Security.IctServiceTokenClientInterceptor>();
            return;
        }

        client.AddCallCredentials(async (context, metadata, serviceProvider) =>
            {
                var token = await serviceProvider.GetRequiredService<Security.IdentityServiceTokenProvider>()
                    .GetTokenAsync(context.CancellationToken).ConfigureAwait(false);
                metadata.Add("Authorization", "Bearer " + token);
            })
            .ConfigureChannel(channel => channel.UnsafeUseInsecureChannelCallCredentials = true);
    }

    /// <summary>
    /// Epic #13316 (HU #13346/#13348): las consultas van directo a core-consultas (<c>CoreConsultas:Address</c>,
    /// obligatoria con el canal de core-api) con el cliente svc-ict (scope platform.consultas), sin respaldo: si no
    /// responde, el error se propaga y el orquestador reintenta (fail-closed). Necesita el token de Identidad
    /// (<c>Ict:ServiceToken:UseIdentity</c>).
    /// </summary>
    internal static void AddConsultasRemoto(IServiceCollection services, IConfiguration configuration, bool useIdentityToken)
    {
        var address = configuration["CoreConsultas:Address"];
        if (!useIdentityToken || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "CoreConsultas:Address necesita una URL absoluta y Ict:ServiceToken:UseIdentity encendida (svc-ict pide su token a Identidad).");
        }

        var deadline = TimeSpan.FromSeconds(configuration.GetValue("CoreConsultas:DeadlineSegundos", 90));
        services.AddGrpcClient<Flit.Consultas.Grpc.V1.ConsultasService.ConsultasServiceClient>(options => options.Address = uri)
            .AddCallCredentials(async (context, metadata, serviceProvider) =>
            {
                var token = await serviceProvider.GetRequiredService<Security.IdentityServiceTokenProvider>()
                    .GetTokenAsync("platform.consultas", context.CancellationToken).ConfigureAwait(false);
                metadata.Add("Authorization", "Bearer " + token);
            })
            .ConfigureChannel(channel => channel.UnsafeUseInsecureChannelCallCredentials = true)
            .AddInterceptor(() => new DeadlinePorDefecto(deadline));
        services.AddScoped<IConsultationClient, ConsultasConsultationClient>();
    }

    /// <summary>Deadline por llamada si quien llama no fija uno (contrato v1.3 §6.1).</summary>
    private sealed class DeadlinePorDefecto(TimeSpan deadline) : Interceptor
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            ArgumentNullException.ThrowIfNull(continuation);
            if (context.Options.Deadline is null)
                context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithDeadline(DateTime.UtcNow.Add(deadline)));

            return continuation(request, context);
        }
    }
}

