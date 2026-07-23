using Flit.Ict.Application.Register;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Grpc.Contracts;
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
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddHttpClient<IIctAttachmentStorage, Storage.FileManagerAttachmentStorage>();

        // Seguridad (login ICT independiente).
        services.AddSingleton(sp => new IctJwtKeyMaterial(sp.GetRequiredService<IOptions<IctJwtSettings>>().Value));
        services.AddSingleton<IIctJwtTokenIssuer, IctRsaJwtTokenIssuer>();
        services.AddSingleton<IIctPasswordHasher, Argon2PasswordHasher>();

        // Repositorios.
        services.AddScoped<IIntegrationClientRepository, IntegrationClientRepository>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();

        // Bootstrap del schema ICT (DDL embebido idempotente al arrancar).
        services.AddHostedService<IctSchemaBootstrapper>();

        // Cliente gRPC hacia core-api (orquestación de creación del borrador). Usado en HU4.
        // TODO(ICT-GRPC-AUTH): adjuntar el service-token (client-credentials) vía interceptor/CallCredentials.
        var grpcAddress = configuration["CoreApiGrpc:Address"];
        if (!string.IsNullOrWhiteSpace(grpcAddress))
        {
            services.AddGrpcClient<IctOrchestration.IctOrchestrationClient>(options =>
                options.Address = new Uri(grpcAddress));
        }

        return services;
    }
}
