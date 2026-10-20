using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Platform.Sdk.Grpc;

/// <summary>Clientes gRPC hacia otros servicios de FLIT (HU #13337, contrato v1.3 §6.1).</summary>
public static class PlatformGrpcClientExtensions
{
    /// <summary>
    /// Registra <typeparamref name="TClient"/> hacia <paramref name="address"/> con token de servicio para
    /// <paramref name="scope"/> (pedido y renovado por <see cref="ServiceTokenProvider"/>), empresa y correlación en la
    /// metadata, deadline por defecto, reintentos ante <c>UNAVAILABLE</c> y circuito. La red interna es h2c: gRPC solo
    /// envía credenciales por un canal sin TLS si se le permite explícitamente.
    /// </summary>
    public static IHttpClientBuilder AddFlitGrpcClient<TClient>(
        this IServiceCollection services,
        IConfiguration configuration,
        Uri address,
        string scope,
        Action<PlatformGrpcClientOptions>? configure = null)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        AddServiceTokens(services, configuration);
        var options = new PlatformGrpcClientOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(sp => new PlatformClientInterceptor<TClient>(
            sp.GetRequiredService<IHttpContextAccessor>(), options, sp.GetRequiredService<TimeProvider>()));
        return services.AddGrpcClient<TClient>(o => o.Address = address)
            .AddInterceptor<PlatformClientInterceptor<TClient>>()
            .AddCallCredentials(async (context, metadata, sp) =>
            {
                var token = await sp.GetRequiredService<ServiceTokenProvider>().GetTokenAsync(scope, context.CancellationToken).ConfigureAwait(false);
                metadata.Add("Authorization", "Bearer " + token);
            })
            .ConfigureChannel(channel => channel.UnsafeUseInsecureChannelCallCredentials = true)
            // traceparent lo pone el interceptor; sin esto HttpClient lo agregaría otra vez.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ActivityHeadersPropagator = null,
                EnableMultipleHttp2Connections = true,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            });
    }

    private static void AddServiceTokens(IServiceCollection services, IConfiguration configuration)
    {
        if (services.Any(d => d.ServiceType == typeof(ServiceTokenProvider)))
            return;

        var section = configuration.GetSection(PlatformServiceClientOptions.SectionName);
        var options = section.Get<PlatformServiceClientOptions>() ?? new PlatformServiceClientOptions();
        if (!Uri.TryCreate(options.TokenEndpoint, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            throw new InvalidOperationException(
                $"{PlatformServiceClientOptions.SectionName} necesita TokenEndpoint, ClientId y ClientSecret para llamar a otros servicios.");
        }

        services.Configure<PlatformServiceClientOptions>(section);
        services.AddHttpClient(ServiceTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ServiceTokenProvider>();
    }
}
