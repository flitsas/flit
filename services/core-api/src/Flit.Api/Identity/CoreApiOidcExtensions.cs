using Flit.Infrastructure.Persistence;

namespace Flit.Api.Identity;

/// <summary>
/// OIDC en core-api (Epic #13217, HU #13232): acepta los tokens del hub sobre <see cref="FlitDbContext"/> y, durante la
/// transición, también atiende el servidor OIDC como respaldo del gateway. En el corte (HU #13235) queda solo la
/// aceptación.
/// </summary>
public static class CoreApiOidcExtensions
{
    public static IServiceCollection AddFlitOidc(this IServiceCollection services, IConfiguration configuration) =>
        services.AddFlitOidcAcceptance<FlitDbContext>(configuration).AddFlitOidcServer(configuration);
}
