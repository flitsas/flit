using System.Diagnostics;
using Flit.Api.Middleware;
using Flit.Platform.Sdk.Tenancy;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Flit.Platform.Sdk.Grpc;

/// <summary>Opciones de un cliente gRPC del SDK (contrato v1.3 §6.1). Los valores por defecto son los del contrato.</summary>
public sealed class PlatformGrpcClientOptions
{
    /// <summary>Deadline de cada llamada si quien llama no fija uno.</summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Reintentos ante <c>UNAVAILABLE</c> (y solo ante ese estado), dentro del mismo deadline.</summary>
    public int MaxRetries { get; set; } = 2;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>El circuito se abre si fallan al menos esta proporción de llamadas…</summary>
    public double FailureRatio { get; set; } = 0.5;

    /// <summary>…en esta ventana…</summary>
    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>…con al menos estas llamadas en la ventana.</summary>
    public int MinimumThroughput { get; set; } = 10;

    /// <summary>Tiempo que el circuito queda abierto: las llamadas fallan de inmediato con <c>UNAVAILABLE</c>.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Interceptor de cliente del SDK (HU #13337): pone en cada llamada la empresa (<c>x-flit-tenant-id</c>) y la correlación
/// (<c>x-correlation-id</c>) de la petición en curso si quien llama no las puso, fija el deadline por defecto y aplica
/// reintentos (solo <c>UNAVAILABLE</c>) y circuito por destino. También <c>traceparent</c>, desde la traza en curso. El
/// token lo pone la credencial de llamada del cliente.
/// </summary>
internal class PlatformClientInterceptor : Interceptor
{
    internal const string CorrelationMetadata = "x-correlation-id";

    private readonly IHttpContextAccessor _http;
    private readonly PlatformGrpcClientOptions _options;
    private readonly ResiliencePipeline _pipeline;

    public PlatformClientInterceptor(IHttpContextAccessor http, PlatformGrpcClientOptions options, TimeProvider time)
    {
        _http = http;
        _options = options;
        _pipeline = BuildPipeline(options, time);
    }

    private static ResiliencePipeline BuildPipeline(PlatformGrpcClientOptions options, TimeProvider time)
    {
        var builder = new ResiliencePipelineBuilder { TimeProvider = time };
        if (options.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetries,
                Delay = options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<RpcException>(e => e.StatusCode == StatusCode.Unavailable),
            });
        }

        return builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = options.FailureRatio,
                SamplingDuration = options.SamplingDuration,
                MinimumThroughput = options.MinimumThroughput,
                BreakDuration = options.BreakDuration,
                ShouldHandle = new PredicateBuilder().Handle<RpcException>(e => e.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded),
            })
            .Build();
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        var prepared = Prepare(context);
        AsyncUnaryCall<TResponse>? last = null;

        var response = ExecuteAsync(prepared.Options.CancellationToken);
        return new AsyncUnaryCall<TResponse>(
            response,
            HeadersAsync(),
            () => last?.GetStatus() ?? new Status(StatusCode.Unavailable, "Circuito abierto: el destino no responde."),
            () => last?.GetTrailers() ?? [],
            () => last?.Dispose());

        async Task<TResponse> ExecuteAsync(CancellationToken ct)
        {
            try
            {
                return await _pipeline.ExecuteAsync(async _ =>
                {
                    last?.Dispose();
                    last = continuation(request, prepared);
                    return await last.ResponseAsync.ConfigureAwait(false);
                }, ct).ConfigureAwait(false);
            }
            catch (BrokenCircuitException ex)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, $"Circuito abierto hacia {prepared.Method.ServiceName}.", ex));
            }
        }

        async Task<Metadata> HeadersAsync()
        {
            try
            {
                await response.ConfigureAwait(false);
            }
            catch (RpcException)
            {
                // Los encabezados del último intento siguen disponibles abajo.
            }

            return last is null ? [] : await last.ResponseHeadersAsync.ConfigureAwait(false);
        }
    }

    private ClientInterceptorContext<TRequest, TResponse> Prepare<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        var headers = context.Options.Headers ?? [];
        var http = _http.HttpContext;

        if (headers.Get(PlatformServiceCallInterceptor.TenantMetadata) is null && http is not null
            && PlatformTenantContext.Resolve(http.User, http.Request.Headers).TenantId is { } tenant)
        {
            headers.Add(PlatformServiceCallInterceptor.TenantMetadata, tenant.ToString());
        }

        if (headers.Get(CorrelationMetadata) is null)
        {
            var correlation = http?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
            headers.Add(CorrelationMetadata, string.IsNullOrEmpty(correlation) ? Guid.CreateVersion7().ToString() : correlation);
        }

        // traceparent desde la traza en curso. El handler HTTP de estos clientes no lo propaga (ver AddFlitGrpcClient):
        // así viaja una sola vez y no depende de quién esté escuchando las trazas.
        if (Activity.Current is { } activity && headers.Get("traceparent") is null)
        {
            DistributedContextPropagator.Current.Inject(activity, headers, static (carrier, key, value) =>
            {
                if (carrier is Metadata metadata && value is not null && metadata.Get(key) is null)
                    metadata.Add(key, value);
            });
        }

        var options = context.Options.WithHeaders(headers);
        if (options.Deadline is null)
            options = options.WithDeadline(DateTime.UtcNow.Add(_options.Deadline));

        return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
    }
}

/// <summary>Un interceptor (y su circuito) por tipo de cliente: un destino caído no abre el circuito de los demás.</summary>
internal sealed class PlatformClientInterceptor<TClient>(IHttpContextAccessor http, PlatformGrpcClientOptions options, TimeProvider time)
    : PlatformClientInterceptor(http, options, time)
    where TClient : class;
