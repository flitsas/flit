using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Routing;

namespace Flit.Platform.Sdk.Grpc;

/// <summary>Servidor gRPC de un servicio de FLIT (HU #13337): errores del §10, salud estándar y reflection solo en DEV.</summary>
public static class PlatformGrpcServerExtensions
{
    public static IGrpcServerBuilder AddFlitGrpcServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var grpc = services.AddGrpc(options => options.Interceptors.Add<PlatformErrorInterceptor>());
        // Estado en el momento de la consulta (sin esperar al publicador periódico) y un chequeo de vida propio; cada
        // servicio agrega los suyos (base, broker) al mismo builder de health checks.
        services.AddGrpcHealthChecks(options => options.UseHealthChecksCache = false)
            .AddCheck("vivo", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());
        services.AddGrpcReflection();
        return grpc;
    }

    /// <summary>
    /// Exige token de servicio con <paramref name="scope"/>, audiencia <paramref name="audience"/> y empresa en la
    /// metadata para cada método de <typeparamref name="TService"/> (<see cref="PlatformServiceCallInterceptor"/>).
    /// </summary>
    public static IGrpcServerBuilder RequireServiceToken<TService>(this IGrpcServerBuilder builder, string scope, string audience)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        return builder.AddServiceOptions<TService>(options => options.Interceptors.Add<PlatformServiceCallInterceptor>(scope, audience));
    }

    /// <summary><c>grpc.health.v1.Health</c> siempre; reflection solo en Development (contrato v1.3 §6.1).</summary>
    public static void MapFlitGrpcPlatform(this IEndpointRouteBuilder endpoints, IHostEnvironment environment, string? requireHost = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(environment);
        var health = endpoints.MapGrpcHealthChecksService();
        if (requireHost is not null)
            health.RequireHost(requireHost);

        if (environment.IsDevelopment())
        {
            var reflection = endpoints.MapGrpcReflectionService();
            if (requireHost is not null)
                reflection.RequireHost(requireHost);
        }
    }
}
