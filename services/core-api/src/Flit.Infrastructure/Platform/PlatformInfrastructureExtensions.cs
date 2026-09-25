using Flit.Infrastructure.Persistence.Repositories.Platform;
using Flit.Modules.Platform;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Infrastructure.Platform;

/// <summary>
/// Cableado del módulo de plataforma de la FLIT Suite (frente B). Se llama con una sola línea desde
/// el bloque <c>FLIT Suite: infraestructura</c> de <c>InfrastructureExtensions</c> (regla R5 de
/// <c>docs/suite/reglas-trabajo-paralelo.md</c>): todo lo del módulo se registra aquí.
/// </summary>
internal static class PlatformInfrastructureExtensions
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services)
    {
        services.AddPlatformModule();
        services.AddScoped<IProductCatalog, ProductCatalogRepository>();
        services.AddScoped<ITenantProductRepository, TenantProductRepository>();
        services.AddScoped<IProductAccessStore, ProductAccessStore>();
        return services;
    }
}
