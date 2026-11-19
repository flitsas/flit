using Flit.Api.Authorization;
using Flit.Api.Grpc;
using Flit.Api.Identity;
using Flit.Infrastructure.Ai.Entrega;
using Flit.Platform.Sdk.Grpc;

namespace Flit.Api.Plataforma.Ai;

/// <summary>
/// Composición de <c>ConfiguracionIaService</c> (ADR-0091): servicio global (sin empresa) que exige token de servicio con
/// <c>aud=tramites</c> y el scope <c>platform.ia.diagnostico</c> (interceptor del SDK) y, por policy, el cliente
/// <c>svc-diagnostico</c>. Siempre mapeado: sin el scope en el cliente de Identidad nadie entra, y sin clave pública
/// registrada responde FAILED_PRECONDITION.
/// </summary>
internal static class ConfiguracionIaExtensions
{
    public static IServiceCollection AddConfiguracionIaDiagnostico(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddEntregaConfiguracionIa();
        services.AddGrpc()
            .RequireServiceTokenSinEmpresa<ConfiguracionIaGrpcService>(DiagnosticoIaServicioAuthorization.Scope, ServiceAudiences.Tramites);
        // Review HU-6 CR-2: el rechazo por la policy de IA también deja ia.llave.denegada. Va después de AddApiSecurity
        // (gana el último registro) y delega en SuperAdminForbiddenResultHandler para el resto de policies.
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, DiagnosticoIaAuthorizationResultHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapConfiguracionIaDiagnostico(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapGrpcService<ConfiguracionIaGrpcService>().RequireAuthorization(DiagnosticoIaServicioAuthorization.Policy);
        return app;
    }
}
