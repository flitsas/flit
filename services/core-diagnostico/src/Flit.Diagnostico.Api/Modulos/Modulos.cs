using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
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

    /// <summary>
    /// Fuentes de consulta (HU #13725, HU #13791): <see cref="Consultas.Fuentes.SeleccionAdaptadores"/> elige al arrancar el
    /// adaptador de todas las fuentes (simulado solo en LOCAL o DEV, real con <c>Diagnostico:Consultas:Address</c>, no conectado
    /// en otro caso; con una configuración inválida el servicio no arranca) y queda registrada como singleton. RUNT conserva su
    /// regla: simulada con la simulación y no conectada en otro caso. Con adaptadores reales se registra el cliente gRPC de
    /// core-consultas (<see cref="Consultas.Fuentes.ClienteConsultas"/>); si no, ninguna llamada sale del servicio. Cada puerto de
    /// fuente se registra con <c>services.AddAdaptador&lt;IPuerto, Real, Simulado, NoConectado&gt;(seleccion)</c>. No mira
    /// <c>IHostEnvironment</c>. Usa TryAdd para que las pruebas puedan poner su doble antes.
    /// </summary>
    public static IServiceCollection AddFuentesConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var seleccion = SeleccionAdaptadores.Desde(configuration);
        services.AddSingleton(seleccion);
        // HU #13792: RUNT persona (persona natural) y RUES (persona con NIT) de las celdas RUNT_PROPIETARIO y RUNT_LOCATARIO.
        services.AddAdaptador<IFuenteRuntPersona, FuenteRuntPersonaReal, FuenteRuntPersonaSimulada, FuenteRuntPersonaNoConectada>(seleccion);
        services.AddAdaptador<IFuenteRues, FuenteRuesReal, FuenteRuesSimulada, FuenteRuesNoConectada>(seleccion);
        // HU #13793: SIMIT por placa (SIMIT_VEHICULO); F05-H5 lo reutiliza por documento (SIMIT_PROPIETARIO/LOCATARIO).
        services.AddAdaptador<IFuenteSimit, FuenteSimitReal, FuenteSimitSimulada, FuenteSimitNoConectada>(seleccion);
        // HU #13795: avalúo comercial (AVALUO) con los datos técnicos que dejó el RUNT.
        services.AddAdaptador<IFuenteAvaluo, FuenteAvaluoReal, FuenteAvaluoSimulada, FuenteAvaluoNoConectada>(seleccion);
        if (seleccion.ModoDe(Domain.Consultas.Fuente.Runt) == ModoAdaptador.Simulado)
            services.TryAddSingleton<IFuenteRunt, FuenteRuntIntempoSimulada>();
        else
            services.TryAddSingleton<IFuenteRunt, FuenteRuntNoConectada>();

        if (seleccion.Modo == ModoAdaptador.Real)
            services.AddClienteConsultas(configuration, seleccion);

        return services;
    }

    /// <summary>
    /// Motor de consultas (HU #13727, ADR a): el encolador real (TryAdd, para que las pruebas pongan su doble antes), la cola,
    /// el ejecutor de la celda RUNT (HU #13730; un ejecutor de la misma fuente registrado antes, el doble de una prueba, lo reemplaza)
    /// los de RUNT_PROPIETARIO y RUNT_LOCATARIO (HU #13792), SIMIT_VEHICULO (HU #13793), SIMIT_PROPIETARIO y SIMIT_LOCATARIO
    /// (HU #13794) y AVALUO (HU #13795),
    /// el registro de adaptadores y el catálogo de fuentes disponibles que de él se deriva (HU #13733), el procesador
    /// y su configuración <see cref="Trabajos.OpcionesProcesador"/> validada al arrancar, y la de reintentos
    /// <see cref="Consultas.OpcionesConsultas"/> (HU #13737), también validada al arrancar. El trabajador en segundo
    /// plano solo se registra con <c>Diagnostico:Procesador:Habilitado=true</c>; no mira <c>IHostEnvironment</c>.
    /// </summary>
    public static IServiceCollection AddMotorConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddScoped<Domain.Consultas.IEncoladorConsultas, Consultas.EncoladorConsultas>();
        services.AddScoped<Trabajos.ColaConsultas>();
        services.AddScoped<Consultas.IEjecutorCelda, Consultas.EjecutorCeldaRunt>();
        // HU #13792: las celdas RUNT de persona (una instancia por fuente; los puertos los registra AddFuentesConsultas).
        services.AddScoped<Consultas.IEjecutorCelda>(sp =>
            ActivatorUtilities.CreateInstance<Consultas.EjecutorCeldaRuntPersona>(sp, Domain.Consultas.Fuente.RuntPropietario));
        services.AddScoped<Consultas.IEjecutorCelda>(sp =>
            ActivatorUtilities.CreateInstance<Consultas.EjecutorCeldaRuntPersona>(sp, Domain.Consultas.Fuente.RuntLocatario));
        // HU #13793: la celda SIMIT_VEHICULO (por la placa definitiva del RUNT).
        services.AddScoped<Consultas.IEjecutorCelda, Consultas.EjecutorCeldaSimitVehiculo>();
        // HU #13794: las celdas SIMIT de persona (una instancia por fuente; por documento de cada propietario o locatario).
        services.AddScoped<Consultas.IEjecutorCelda>(sp =>
            ActivatorUtilities.CreateInstance<Consultas.EjecutorCeldaSimitPersona>(sp, Domain.Consultas.Fuente.SimitPropietario));
        services.AddScoped<Consultas.IEjecutorCelda>(sp =>
            ActivatorUtilities.CreateInstance<Consultas.EjecutorCeldaSimitPersona>(sp, Domain.Consultas.Fuente.SimitLocatario));
        // HU #13795: la celda AVALUO (placa y VIN del RUNT con modelo, cilindraje, combustible y pasajeros).
        services.AddScoped<Consultas.IEjecutorCelda, Consultas.EjecutorCeldaAvaluo>();
        // HU #13733: las fuentes con ejecutor son las únicas que se pueden habilitar o pedir (TryAdd: las pruebas ponen su doble).
        services.TryAddSingleton<Consultas.IRegistroAdaptadores, Consultas.RegistroAdaptadores>();
        services.AddSingleton<Consultas.FuentesDisponibles>();
        services.AddTransient<Trabajos.ProcesadorConsultas>();
        // HU #13737 (D-12): reintentos de transitorios, configuración global validada al arrancar en [0, 3].
        services.AddOptions<Consultas.OpcionesConsultas>()
            .Bind(configuration.GetSection(Consultas.OpcionesConsultas.Seccion))
            .Validate(o => o.EsValida(), Consultas.OpcionesConsultas.MensajeFueraDeRango)
            .ValidateOnStart();
        services.AddOptions<Trabajos.OpcionesProcesador>()
            .Bind(configuration.GetSection(Trabajos.OpcionesProcesador.Seccion))
            .Validate(o => o.Errores().Count == 0, $"{Trabajos.OpcionesProcesador.Seccion} tiene valores fuera de rango (Intervalo > 0, TamanoLote 1-500, DuracionLease >= 10 s, VehiculosEnVueloPorEmpresa >= 1).")
            .ValidateOnStart();
        if (configuration.GetValue($"{Trabajos.OpcionesProcesador.Seccion}:{nameof(Trabajos.OpcionesProcesador.Habilitado)}", false))
            services.AddHostedService<Trabajos.TrabajadorConsultas>();
        return services;
    }

    /// <summary>
    /// Evidencias (HU #13846): el almacén de archivos (file-manager corporativo con <c>FILE_MANAGER_*</c>; la base solo con
    /// <see cref="Infraestructura.Archivos.OpcionesAlmacen.GuardarEnBase"/>; sin ninguno el servicio no arranca) y el registro de
    /// evidencias con huella que usan el PDF, el Excel y el ZIP de F07.
    /// </summary>
    public static IServiceCollection AddEvidencias(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Infraestructura.Archivos.AlmacenArchivosExtensions.AddAlmacenArchivos(services, configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Reportes.ServicioEvidencias>();
        services.AddScoped<Reportes.LectorContenidoReporte>();
        // HU #13848: PDF corporativo del vehículo (contenido de F07-H2 + huella y registro de F07-H1).
        services.AddScoped<Reportes.DescargaPdfVehiculo>();
        // HU #13849: Excel del lote (mismo contenido de F07-H2, huella del lote y registro de F07-H1).
        services.AddScoped<Reportes.DescargaExcelLote>();
        return services;
    }

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
        Tablero.EndpointsTablero.Map(api);
        Consultas.EndpointsReproceso.Map(api);
        Reportes.ReportesEndpoints.Map(api);
        api.MapFallback(() => Results.NotFound());
        return api;
    }
}
