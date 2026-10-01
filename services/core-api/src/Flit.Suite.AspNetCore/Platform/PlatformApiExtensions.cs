using Flit.Modules.Platform.Application.Hosts;

namespace Flit.Api.Platform;

/// <summary>
/// Registro de la API de plataforma de la FLIT Suite (frente B). Una línea en el bloque
/// <c>FLIT Suite: servicios</c> de <c>Program.cs</c> (regla R5). HU #12966 (B-06).
/// </summary>
public static class PlatformApiExtensions
{
    public static IServiceCollection AddPlatformApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SuiteHostsOptions>(configuration.GetSection(SuiteHostsOptions.SectionName));
        services.Configure<ProductAccessOptions>(configuration.GetSection(ProductAccessOptions.SectionName));
        services.AddMemoryCache();
        services.AddSingleton<IProductHosts, ConfiguredProductHosts>();
        return services;
    }
}
