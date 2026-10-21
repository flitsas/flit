using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Modulos;

/// <summary>
/// Rutas de negocio de Diagnóstico bajo <see cref="Program.Prefijo"/> (HU #13724). Todo el grupo exige un token del
/// producto (audiencia diagnostico). Cada módulo suma aquí su <c>Map</c>; lo que no existe responde 404 a quien sí está
/// autenticado (el fallback tiene la prioridad más baja, así que nunca tapa una ruta mapeada).
/// </summary>
public static class Modulos
{
    /// <summary>Habilita las fuentes simuladas (por defecto false). Solo se admite con <see cref="ClaveAmbiente"/> LOCAL o DEV.</summary>
    public const string ClaveSimulacionHabilitada = "Diagnostico:Simulacion:Habilitada";

    /// <summary>
    /// Ambiente lógico del despliegue: LOCAL, DEV, QA o PDN (sin valor por defecto). Existe porque los tres ambientes corren
    /// con ASPNETCORE_ENVIRONMENT=Development, así que el entorno del host no distingue DEV de QA o PDN.
    /// </summary>
    public const string ClaveAmbiente = "Diagnostico:Ambiente";

    private static readonly string[] AmbientesConSimulacion = ["LOCAL", "DEV"];

    /// <summary>
    /// Fuentes de consulta (HU #13725): la RUNT del vehículo es la no conectada salvo que se pida la simulación de forma
    /// explícita, y solo en LOCAL o DEV; en otro ambiente (o sin <see cref="ClaveAmbiente"/>) el servicio no arranca. No mira
    /// <c>IHostEnvironment</c>. Usa TryAdd para que las pruebas puedan poner su doble antes.
    /// </summary>
    public static IServiceCollection AddFuentesConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue(ClaveSimulacionHabilitada, false))
        {
            services.TryAddSingleton<Consultas.Fuentes.IFuenteRunt, Consultas.Fuentes.FuenteRuntNoConectada>();
            return services;
        }

        var ambiente = configuration[ClaveAmbiente]?.Trim();
        if (!AmbientesConSimulacion.Contains(ambiente, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"core-{ServicioSettings.Codigo} no puede arrancar: {Variable(ClaveSimulacionHabilitada)}=true solo se admite con " +
                $"{Variable(ClaveAmbiente)}=DEV o LOCAL (valor actual: «{(string.IsNullOrEmpty(ambiente) ? "sin definir" : ambiente)}»). " +
                $"En QA y PDN quite {Variable(ClaveSimulacionHabilitada)}: la fuente RUNT es la no conectada hasta C-PER. " +
                "Ver services/core-diagnostico/README.md.");
        }

        services.TryAddSingleton<Consultas.Fuentes.IFuenteRunt, Consultas.Fuentes.FuenteRuntIntempoSimulada>();
        return services;
    }

    /// <summary>
    /// Motor de consultas (HU #13727, ADR a): el encolador real (TryAdd, para que las pruebas pongan su doble antes), la cola,
    /// el ejecutor de la celda RUNT (HU #13730; un ejecutor de la misma fuente registrado antes, el doble de una prueba, lo reemplaza),
    /// el registro de adaptadores y el catálogo de fuentes disponibles que de él se deriva (HU #13733), el procesador
    /// y su configuración <see cref="Trabajos.OpcionesProcesador"/> validada al arrancar. El trabajador en segundo
    /// plano solo se registra con <c>Diagnostico:Procesador:Habilitado=true</c>; no mira <c>IHostEnvironment</c>.
    /// </summary>
    public static IServiceCollection AddMotorConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddScoped<Domain.Consultas.IEncoladorConsultas, Consultas.EncoladorConsultas>();
        services.AddScoped<Trabajos.ColaConsultas>();
        services.AddScoped<Consultas.IEjecutorCelda, Consultas.EjecutorCeldaRunt>();
        // HU #13733: las fuentes con ejecutor son las únicas que se pueden habilitar o pedir (TryAdd: las pruebas ponen su doble).
        services.TryAddSingleton<Consultas.IRegistroAdaptadores, Consultas.RegistroAdaptadores>();
        services.AddSingleton<Consultas.FuentesDisponibles>();
        services.AddTransient<Trabajos.ProcesadorConsultas>();
        services.AddOptions<Trabajos.OpcionesProcesador>()
            .Bind(configuration.GetSection(Trabajos.OpcionesProcesador.Seccion))
            .Validate(o => o.Errores().Count == 0, $"{Trabajos.OpcionesProcesador.Seccion} tiene valores fuera de rango (Intervalo > 0, TamanoLote 1-500, DuracionLease >= 10 s, VehiculosEnVueloPorEmpresa >= 1).")
            .ValidateOnStart();
        if (configuration.GetValue($"{Trabajos.OpcionesProcesador.Seccion}:{nameof(Trabajos.OpcionesProcesador.Habilitado)}", false))
            services.AddHostedService<Trabajos.TrabajadorConsultas>();
        return services;
    }

    private static string Variable(string clave) => clave.Replace(":", "__", StringComparison.Ordinal);

    public static RouteGroupBuilder MapModulosDiagnostico(this WebApplication app, string prefijo)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup(prefijo).RequireAuthorization();
        Sesion.SesionEndpoints.Map(api);
        Configuracion.ConfiguracionEndpoints.Map(api);
        Backoffice.BackofficeEndpoints.Map(api);
        Catalogos.TiposTramiteEndpoints.Map(api);
        Diagnosticos.DiagnosticosEndpoints.Map(api);
        Carga.CargaEndpoints.Map(api);
        Vehiculos.VehiculosEndpoints.Map(api);
        Express.ExpressEndpoints.Map(api);
        api.MapFallback(() => Results.NotFound());
        return api;
    }
}
