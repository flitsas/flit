using Flit.Modules.Platform.Application.Access;
using Flit.Modules.Platform.Application.TenantProducts;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Modules.Platform;

/// <summary>
/// Registro de los casos de uso del módulo de plataforma (FLIT Suite, frente B). Los handlers se
/// inyectan por tipo concreto, sin MediatR, como el resto del repo.
/// </summary>
/// <remarks>
/// Los puertos (<c>IProductCatalog</c>, <c>ITenantProductRepository</c>) viven en
/// <c>Flit.Infrastructure</c> y los registra <c>AddPlatformInfrastructure()</c>.
/// </remarks>
public static class PlatformModuleExtensions
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services)
    {
        services.AddScoped<SetTenantProductEnabledHandler>();
        services.AddScoped<ListTenantProductsHandler>();
        // HU #12965 (B-05): implementación real del contrato §4.
        services.AddScoped<IProductAccessResolver, ProductAccessResolver>();
        return services;
    }
}
