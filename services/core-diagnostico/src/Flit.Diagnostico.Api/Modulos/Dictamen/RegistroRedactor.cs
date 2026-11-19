using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;
using Flit.Platform.Grpc.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Dictamen;

/// <summary>
/// Registro del redactor (épica #12678; ADR-0091 §3.1). <c>Diagnostico:Ia:Origen</c>:
/// <list type="bullet">
/// <item><c>local</c> (por defecto): con <c>Diagnostico:Anthropic:ApiKey</c> usa Anthropic. Sin llave, el simulado solo entra en
/// <c>Diagnostico:Ambiente</c> LOCAL o DEV y con <c>Diagnostico:Anthropic:Simulado</c> o <c>Diagnostico:Simulacion:Habilitada</c>. En QA,
/// PDN o sin ambiente no se simula.</item>
/// <item><c>plataforma</c> (servidores): proveedor, modelo y llave de «Configuración de IA» de core-api por gRPC, con la llave cifrada para
/// la privada de Diagnóstico. <b>Nunca simula.</b> Sin <c>Diagnostico:Tramites:Address</c> o sin una privada RSA-3072 y un <c>kid</c>
/// válidos queda sin configurar (plantilla) y lo avisa al arrancar. <c>BaseUrl</c> sigue siendo local.</item>
/// </list>
/// Cualquier otro origen queda sin configurar (fallo cerrado).
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
        services.Configure<OpcionesIa>(configuration.GetSection(OpcionesIa.Seccion));
        services.AddScoped<ServicioRedactor>();
        services.AddHostedService<AvisoArranqueRedactor>();

        var origen = configuration[$"{OpcionesIa.Seccion}:Origen"]?.Trim();
        if (string.IsNullOrEmpty(origen) || string.Equals(origen, OpcionesIa.OrigenLocal, StringComparison.OrdinalIgnoreCase))
            return AddLocal(services, configuration);
        if (string.Equals(origen, OpcionesIa.OrigenPlataforma, StringComparison.OrdinalIgnoreCase))
            return AddPlataforma(services, configuration);

        services.AddSingleton<IProveedorRedactor, ProveedorRedactorNoConfigurado>();
        services.AddSingleton(new AvisoArranqueRedactor.Estado("desconocido", "no-configurado", string.Empty, false, "ORIGEN_DESCONOCIDO", null));
        return services;
    }

    private static IServiceCollection AddLocal(IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = configuration[$"{OpcionesAnthropic.Seccion}:ApiKey"]?.Trim();
        string proveedor;
        if (!string.IsNullOrEmpty(apiKey))
        {
            services.AddSingleton<IFuenteCredencialRedactor, FuenteCredencialLocal>();
            services.AddSingleton(TopeLlamadasRedactor.SinTope());
            services.AddHttpClient<IProveedorRedactor, ProveedorRedactorAnthropic>();
            proveedor = "anthropic";
        }
        else if (SimulacionHabilitada(configuration))
        {
            services.AddSingleton<IProveedorRedactor, ProveedorRedactorSimulado>();
            proveedor = "simulado";
        }
        else
        {
            services.AddSingleton<IProveedorRedactor, ProveedorRedactorNoConfigurado>();
            proveedor = "no-configurado";
        }

        services.AddSingleton(new AvisoArranqueRedactor.Estado(OpcionesIa.OrigenLocal, proveedor, string.Empty, false, "OK", null));
        return services;
    }

    private static IServiceCollection AddPlataforma(IServiceCollection services, IConfiguration configuration)
    {
        var opciones = configuration.GetSection(OpcionesIa.Seccion).Get<OpcionesIa>() ?? new OpcionesIa();
        var privadaPresente = !string.IsNullOrWhiteSpace(opciones.ClavePrivadaArchivo);
        var kid = SobreLlaveIa.KidValido(opciones.Kid?.Trim()) ? opciones.Kid!.Trim() : string.Empty;
        var direccion = configuration[TiposTramiteEndpoints.ClaveDireccion];
        string motivo;
        string? avisoAnterior = null;
        ClavesPrivadasIa? claves = null;
        if (string.IsNullOrWhiteSpace(direccion) || !Uri.TryCreate(direccion, UriKind.Absolute, out var uri))
        {
            motivo = "SIN_DIRECCION";
            uri = null;
        }
        else
        {
            claves = ClavesPrivadasIa.Cargar(opciones, out motivo, out avisoAnterior);
        }

        if (claves is null || uri is null)
        {
            services.AddSingleton<IProveedorRedactor, ProveedorRedactorNoConfigurado>();
            services.AddSingleton(new AvisoArranqueRedactor.Estado(OpcionesIa.OrigenPlataforma, "no-configurado", kid, privadaPresente, motivo, avisoAnterior));
            return services;
        }

        services.AddSingleton(claves);
        services.TryAddSingleton(TimeProvider.System);
        services.AddClienteConfiguracionIa(configuration, uri, TimeSpan.FromSeconds(Math.Clamp(opciones.PlazoRpcSegundos, 1, 60)));
        services.AddSingleton<IFuenteCredencialRedactor>(sp => new FuenteCredencialPlataforma(
            sp.GetRequiredService<ConfiguracionIaService.ConfiguracionIaServiceClient>,
            sp.GetRequiredService<ClavesPrivadasIa>(),
            sp.GetRequiredService<IOptions<OpcionesIa>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<FuenteCredencialPlataforma>>()));
        services.AddSingleton(sp => new TopeLlamadasRedactor(opciones.MaxLlamadasPorMinuto, sp.GetRequiredService<TimeProvider>()));
        services.AddHttpClient<IProveedorRedactor, ProveedorRedactorAnthropic>();
        services.AddSingleton(new AvisoArranqueRedactor.Estado(OpcionesIa.OrigenPlataforma, FuenteCredencialPlataforma.ProveedorPermitido, claves.Kid, privadaPresente, "OK", avisoAnterior));
        return services;
    }
}
