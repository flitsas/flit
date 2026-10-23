using Flit.Admin.Application.Integrations.Auth;
using Flit.Admin.Domain.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Infrastructure.Security;

/// <summary>HU #13087 (Épica #12737) — registro del pase de los clientes de integración externos.</summary>
internal static class ExternalClientAuthExtensions
{
    public static IServiceCollection AddExternalClientAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration.GetSection(ExternalJwtSettings.SectionName).Get<ExternalJwtSettings>()
            ?? new ExternalJwtSettings());
        services.AddSingleton(configuration.GetSection(ExternalClientAuthSettings.SectionName).Get<ExternalClientAuthSettings>()
            ?? new ExternalClientAuthSettings());
        services.AddSingleton<ExternalJwtKeyMaterial>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IExternalClientTokenIssuer, ExternalJwtTokenIssuer>();
        // Scoped: sigue la vida de IPasswordHasher que haya registrada (singleton en el host, scoped en algunos tests).
        services.AddScoped<IExternalClientSecretHasher, ExternalClientSecretHasher>();
        return services;
    }
}
