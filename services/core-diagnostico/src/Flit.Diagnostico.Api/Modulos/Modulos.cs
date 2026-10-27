using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Platform.Sdk.Messaging;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Diagnostico.Api.Modulos;

/// <summary>
/// Rutas de negocio de Diagnóstico bajo <see cref="Program.Prefijo"/> (HU #13724). Todo el grupo exige un token del
/// producto (audiencia diagnostico). Cada módulo suma aquí su <c>Map</c>; lo que no existe responde 404 a quien sí está
/// autenticado (el fallback tiene la prioridad más baja, así que nunca tapa una ruta mapeada).
/// </summary>
public static class Modulos
{
    /// <summary>
    /// Ambiente lógico del despliegue: LOCAL, DEV, QA o PDN (sin valor por defecto). Existe porque los tres ambientes corren
    /// con ASPNETCORE_ENVIRONMENT=Development, así que el entorno del host no distingue DEV de QA o PDN.
    /// </summary>
    public const string ClaveAmbiente = "Diagnostico:Ambiente";

    /// <summary>
    /// Lo que sigue en línea con core-consultas (ADR-0075, acta §3.1): desde la tanda B Diagnóstico no tiene adaptadores de
    /// proveedor ni simulación (la de LOCAL y DEV vive en core-consultas, U3 = a) y las consultas de las celdas van por el bus
    /// (<see cref="AddMotorConsultas"/>). Aquí queda la réplica diaria de los departamentos con impuesto soportado (HU #13861,
    /// capacidad global, Q4=a): con <c>Diagnostico:Consultas:Address</c> por el rpc <c>ListarDepartamentosImpuesto</c>
    /// (<see cref="Consultas.Fuentes.ClienteConsultas"/>); sin dirección, la capacidad no conectada (la réplica queda como está). Una
    /// dirección o un deadline inválidos impiden arrancar. El trabajador de la réplica se registra siempre. Usa TryAdd para que las
    /// pruebas puedan poner su doble antes.
    /// </summary>
    public static IServiceCollection AddFuentesConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (services.AddClienteConsultas(configuration))
            services.TryAddScoped<Catalogos.ICapacidadImpuestoPlataforma, Catalogos.CapacidadImpuestoGrpc>();
        else
            services.TryAddScoped<Catalogos.ICapacidadImpuestoPlataforma, Catalogos.CapacidadImpuestoNoConectada>();
        services.TryAddSingleton<Catalogos.EstadoCapacidadImpuesto>();
        services.TryAddScoped<Catalogos.ISincronizacionDepartamentosImpuesto, Catalogos.SincronizacionDepartamentosImpuesto>();
        services.AddHostedService<Catalogos.TrabajadorSincronizacionDepartamentosImpuesto>();
        return services;
    }

    /// <summary>
    /// Motor de consultas por RabbitMQ (HU #13727, ADR-0075): el encolador real (TryAdd, para que las pruebas pongan su doble
    /// antes), los ejecutores de cada fuente («construir solicitud / aplicar respuesta»: RUNT, HU #13730; RUNT_PROPIETARIO y
    /// RUNT_LOCATARIO, HU #13792; SIMIT_VEHICULO, HU #13793; SIMIT_PROPIETARIO y SIMIT_LOCATARIO, HU #13794; AVALUO, HU #13795;
    /// IMPUESTO_DEPARTAMENTAL, HU #13862; un ejecutor de la misma fuente registrado antes, el doble de una prueba, lo reemplaza), un
    /// constructor del <c>*Request</c> por fuente, el publicador de celdas, los reintentos (HU #13737,
    /// <see cref="Consultas.OpcionesConsultas"/> validada al arrancar), el registro de adaptadores y el catálogo de fuentes
    /// disponibles (HU #13733). El consumidor de <c>consultas.resultado.diagnostico</c> (cola <see cref="Consultas.Bus.OpcionesBus.Cola"/>,
    /// solo del productor <c>consultas</c>, con la concurrencia y el prefetch de <see cref="Consultas.Bus.OpcionesBus"/> y la cola de
    /// reintentos de 5, 10 y 20 s) y el reconciliador (<see cref="Trabajos.ReconciliadorConsultas"/>, D3, con
    /// <see cref="Trabajos.OpcionesReconciliador"/> validada al arrancar) se registran solo con
    /// <c>Diagnostico:Consultas:Bus:ConsumidorHabilitado=true</c>; no mira <c>IHostEnvironment</c>.
    /// </summary>
    public static IServiceCollection AddMotorConsultas(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddScoped<Domain.Consultas.IEncoladorConsultas, Consultas.EncoladorConsultas>();
        services.AddScoped<Trabajos.ColaConsultas>();
        services.AddScoped<Consultas.IEjecutorCelda, Consultas.EjecutorCeldaRunt>();
        // HU #13792: las celdas RUNT de persona (una instancia por fuente).
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
        // HU #13862: la celda IMPUESTO_DEPARTAMENTAL (placa del RUNT y departamento derivado, si sigue soportado en la réplica).
        services.AddScoped<Consultas.IEjecutorCelda, Consultas.EjecutorCeldaImpuesto>();
        // HU #13733: las fuentes con ejecutor son las únicas que se pueden habilitar o pedir (TryAdd: las pruebas ponen su doble).
        services.TryAddSingleton<Consultas.IRegistroAdaptadores, Consultas.RegistroAdaptadores>();
        services.AddSingleton<Consultas.FuentesDisponibles>();
        // ADR-0075 (HU #13727): un constructor del *Request por fuente y el publicador de celdas, que deja el trabajo en la outbox
        // del scope (la del contexto con la empresa fijada por EmpresaDelTrabajo) o cierra la celda.
        services.AddScoped<Consultas.Bus.IConstructorSolicitud, Consultas.Bus.ConstructorSolicitudRunt>();
        foreach (var fuente in new[] { Domain.Consultas.Fuente.RuntPropietario, Domain.Consultas.Fuente.RuntLocatario })
            services.AddScoped<Consultas.Bus.IConstructorSolicitud>(sp => ActivatorUtilities.CreateInstance<Consultas.Bus.ConstructorSolicitudRuntPersona>(sp, fuente));
        services.AddScoped<Consultas.Bus.IConstructorSolicitud, Consultas.Bus.ConstructorSolicitudSimitVehiculo>();
        foreach (var fuente in new[] { Domain.Consultas.Fuente.SimitPropietario, Domain.Consultas.Fuente.SimitLocatario })
            services.AddScoped<Consultas.Bus.IConstructorSolicitud>(sp => ActivatorUtilities.CreateInstance<Consultas.Bus.ConstructorSolicitudSimitPersona>(sp, fuente));
        services.AddScoped<Consultas.Bus.IConstructorSolicitud, Consultas.Bus.ConstructorSolicitudAvaluo>();
        services.AddScoped<Consultas.Bus.IConstructorSolicitud, Consultas.Bus.ConstructorSolicitudImpuesto>();
        services.AddScoped<Consultas.Bus.PublicadorCeldas>();
        // HU #13737 (ADR-0075, Q4): los reintentos los decide Diagnóstico; core-consultas consulta una sola vez.
        services.AddSingleton<Consultas.Bus.ProgramadorReintentos>();
        // HU #13737 (D-12): reintentos de transitorios, configuración global validada al arrancar en [0, 3].
        services.AddOptions<Consultas.OpcionesConsultas>()
            .Bind(configuration.GetSection(Consultas.OpcionesConsultas.Seccion))
            .Validate(o => o.EsValida(), Consultas.OpcionesConsultas.MensajeFueraDeRango)
            .ValidateOnStart();
        // ADR-0075 (HU #13727, T9): el consumidor de los resultados de core-consultas y su señal de vida para el reconciliador (T11).
        services.TryAddScoped<Consultas.Bus.ConsumidorResultados>();
        services.TryAddSingleton<Consultas.Bus.PulsoConsultas>();
        services.AddOptions<Trabajos.OpcionesReconciliador>()
            .Bind(configuration.GetSection(Trabajos.OpcionesReconciliador.Seccion))
            .Validate(o => o.Errores().Count == 0, $"{Trabajos.OpcionesReconciliador.Seccion} tiene valores fuera de rango (Intervalo > 0, Margen >= 1 min, VentanaVida > 0, Maximo 1-5000, TtlTrabajo > Margen, 1 min <= ToleranciaOrden < TtlTrabajo).")
            .ValidateOnStart();
        services.AddSingleton<Trabajos.ReconciliadorConsultas>();
        var bus = new Consultas.Bus.OpcionesBus();
        configuration.GetSection(Consultas.Bus.OpcionesBus.Seccion).Bind(bus);
        if (bus.Errores() is { Count: > 0 } errores)
            throw new InvalidOperationException($"{Consultas.Bus.OpcionesBus.Seccion} tiene valores fuera de rango: {string.Join("; ", errores)}.");
        if (bus.ConsumidorHabilitado)
        {
            services.AddFlitConsumer<Persistence.DiagnosticoDb, Consultas.Bus.ConsumidorResultados, Consultas.Bus.ResultadoConsultaDiagnostico>(
                configuration, Consultas.Bus.OpcionesBus.Cola, Consultas.Bus.OpcionesBus.Productor, [Consultas.Bus.ResultadoConsultaDiagnostico.Tipo], o =>
                {
                    o.Concurrencia = bus.Concurrencia;
                    o.Prefetch = bus.Prefetch;
                    // Q3 = 3a: solo core-consultas (user_id validado por el broker) publica los resultados.
                    o.ProductoresPermitidos.Add(Consultas.Bus.OpcionesBus.Productor);
                    // Q4 (U2 = a): la espera del resultado demorado es la de PoliticaReintentos (5, 10 y 20 s).
                    o.RetryDelays.Clear();
                    foreach (var espera in Consultas.Bus.ProgramadorReintentos.Esperas)
                        o.RetryDelays.Add(espera);
                });
            // D3 (6r-i, U4 = a): la red de seguridad de los trabajos perdidos corre con el consumidor.
            services.AddHostedService(sp => sp.GetRequiredService<Trabajos.ReconciliadorConsultas>());
        }

        return services;
    }

    /// <summary>
    /// Evidencias (HU #13846): el almacén de archivos (file-manager corporativo con <c>FILE_MANAGER_*</c>; la base solo con
    /// <see cref="Infraestructura.Archivos.OpcionesAlmacen.GuardarEnBase"/>; sin ninguno el servicio no arranca) y el registro de
    /// evidencias con huella que usan el PDF, el Excel y el ZIP de F07. El ZIP (HU #13850) va por su cola con
    /// <see cref="Trabajos.OpcionesEvidencias"/> validada al arrancar.
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
        // HU #13850: ZIP de los PDF del lote en segundo plano (pedido/estado/archivo, cola entre compañías y empaquetador con la
        // empresa de cada ZIP fijada en su contexto). El trabajador solo se registra con Diagnostico:Evidencias:Habilitado=true.
        services.AddScoped<Reportes.SolicitudZip>();
        services.AddScoped<Trabajos.ColaEvidencias>();
        services.AddSingleton<Reportes.EmpaquetadorZip>();
        services.AddOptions<Trabajos.OpcionesEvidencias>()
            .Bind(configuration.GetSection(Trabajos.OpcionesEvidencias.Seccion))
            .Validate(o => o.Errores().Count == 0, $"{Trabajos.OpcionesEvidencias.Seccion} tiene valores fuera de rango (Intervalo > 0, DuracionLease >= 30 s, MaxReintentos 0-5, EsperaReintento > 0).")
            .ValidateOnStart();
        if (configuration.GetValue($"{Trabajos.OpcionesEvidencias.Seccion}:{nameof(Trabajos.OpcionesEvidencias.Habilitado)}", false))
            services.AddHostedService<Trabajos.TrabajadorEvidencias>();
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
        Reportes.EvidenciasEndpoints.Map(api);
        api.MapFallback(() => Results.NotFound());
        return api;
    }
}
