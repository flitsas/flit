using System.Threading.RateLimiting;
using Flit.Api.Authorization;
using Flit.Api.Grpc;
using Flit.Api.Identity;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Api.Derivacion;

/// <summary>
/// ADR-0084 §3 regla 6 — techo de llamadas simultáneas al rpc de Diagnóstico. <see cref="ConcurrencyLimiter"/> con cola 0:
/// sin permiso libre la llamada se rechaza al instante (el adaptador responde <c>RESOURCE_EXHAUSTED</c>) en vez de esperar;
/// la espera y el reintento con espera creciente son del consumidor de Diagnóstico. Singleton por proceso.
/// </summary>
internal sealed class LimitadorDerivacion(int permisos) : IDisposable
{
    private readonly ConcurrencyLimiter _limitador = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = Math.Max(1, permisos),
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public int Permisos { get; } = Math.Max(1, permisos);

    /// <summary>Un permiso si hay libre (<see cref="RateLimitLease.IsAcquired"/>); se devuelve al disponer el lease.</summary>
    public RateLimitLease Intentar() => _limitador.AttemptAcquire(1);

    public void Dispose() => _limitador.Dispose();
}

/// <summary>
/// Composición del rpc <c>DerivacionTramitesService</c> (ADR-0084, Punto 4 incremento 1). Configuración
/// <c>Tramites:Derivacion</c>: <c>Habilitada</c> (apagada por defecto; sin ella el servicio no se mapea y nadie lo
/// atiende: el host responde UNIMPLEMENTED o UNAUTHENTICATED, ambos no reintentables para Diagnóstico) y <c>ConcurrenciaMaxima</c> (8; debe ser ≥ la suma de <c>Diagnostico:Derivacion:Concurrencia</c> de las
/// réplicas de Diagnóstico). Las dependencias se registran siempre (son baratas); el mapeo es lo que se condiciona.
/// </summary>
internal static class DerivacionExtensions
{
    public const string ClaveHabilitada = "Tramites:Derivacion:Habilitada";
    public const string ClaveConcurrencia = "Tramites:Derivacion:ConcurrenciaMaxima";
    public const int ConcurrenciaPorDefecto = 8;

    public static IServiceCollection AddDerivacionDiagnostico(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var concurrencia = configuration.GetValue(ClaveConcurrencia, ConcurrenciaPorDefecto);
        if (concurrencia < 1)
            throw new InvalidOperationException($"{ClaveConcurrencia} debe ser al menos 1 (valor: {concurrencia}).");

        services.AddSingleton(_ => new LimitadorDerivacion(concurrencia));
        services.AddScoped<IAlmacenBorradorDiagnostico, AlmacenBorradorDiagnostico>();
        services.AddScoped<IAutorDerivacion, AutorDerivacion>();
        services.AddScoped<CreadorBorradorDiagnostico>();

        // El interceptor del SDK valida token de servicio, audiencia tramites, el scope y la empresa en x-flit-tenant-id.
        services.AddGrpc().RequireServiceToken<DerivacionTramitesGrpcService>(DiagnosticoDerivacionServicioAuthorization.Scope, ServiceAudiences.Tramites);
        return services;
    }

    /// <summary>Mapea el rpc solo con <see cref="ClaveHabilitada"/>, exigiendo la policy del cliente <c>svc-diagnostico</c>.</summary>
    public static IEndpointRouteBuilder MapDerivacionDiagnostico(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (app.ServiceProvider.GetRequiredService<IConfiguration>().GetValue(ClaveHabilitada, false))
        {
            app.MapGrpcService<DerivacionTramitesGrpcService>()
                .RequireAuthorization(DiagnosticoDerivacionServicioAuthorization.Policy);
        }

        return app;
    }
}
