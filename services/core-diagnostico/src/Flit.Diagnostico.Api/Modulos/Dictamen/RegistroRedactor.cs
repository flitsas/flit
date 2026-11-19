using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Registro del redactor (épica #12678). Con <c>Diagnostico:Anthropic:ApiKey</c> usa Anthropic. Sin llave, el simulado
/// solo entra en <c>Diagnostico:Ambiente</c> LOCAL o DEV y con <c>Diagnostico:Anthropic:Simulado</c> o
/// <c>Diagnostico:Simulacion:Habilitada</c>. En QA, PDN o sin ambiente no se simula.
/// </summary>
public static class RegistroRedactor
{
    public static bool SimulacionHabilitada(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ambiente = configuration[Modulos.ClaveAmbiente];
        var localODev = string.Equals(ambiente, "LOCAL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ambiente, "DEV", StringComparison.OrdinalIgnoreCase);
        if (!localODev)
            return false;
        if (bool.TryParse(configuration[$"{OpcionesAnthropic.Seccion}:Simulado"], out var propio) && propio)
            return true;
        return bool.TryParse(configuration["Diagnostico:Simulacion:Habilitada"], out var fuentes) && fuentes;
    }

    public static IServiceCollection AddRedactorDictamen(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<OpcionesAnthropic>(configuration.GetSection(OpcionesAnthropic.Seccion));
        services.AddScoped<ServicioRedactor>();
        var apiKey = configuration[$"{OpcionesAnthropic.Seccion}:ApiKey"]?.Trim();
        if (!string.IsNullOrEmpty(apiKey))
            services.AddHttpClient<IProveedorRedactor, ProveedorRedactorAnthropic>();
        else if (SimulacionHabilitada(configuration))
            services.AddSingleton<IProveedorRedactor, ProveedorRedactorSimulado>();
        else
            services.AddSingleton<IProveedorRedactor, ProveedorRedactorNoConfigurado>();
        return services;
    }
}
