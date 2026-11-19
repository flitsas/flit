using Flit.Admin.Application.Plataforma.Ai.Entrega;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.Ai.Entrega;

/// <summary>Registro de la entrega de configuración de IA con la llave sellada (ADR-0091).</summary>
public static class EntregaConfiguracionIaExtensions
{
    public static IServiceCollection AddEntregaConfiguracionIa(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<RegistroClavesDestinatarioIa>();
        services.AddSingleton<ILectorLlaveProveedorIa, AiApiKeyEnvironmentProbe>();
        services.AddScoped<IEntregaConfiguracionIa, ServicioEntregaConfiguracionIa>();
        return services;
    }
}
