using Flit.Comparendos.Api.Modulos.Portal;
using Flit.Platform.Sdk.Grpc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Comparendos.Api.Modulos;

/// <summary>Registro y mapeo de los módulos del producto. Cada módulo agrega aquí sus servicios y sus endpoints.</summary>
public static class Modulos
{
    /// <summary>
    /// Modo simulado (fuentes, API de contratos y OCR sin llave): solo donde se pide de forma explícita con
    /// <c>Comparendos:Simulacion:Habilitada</c>, o por defecto en Development, Dev y Local. En QA y PDN, lo que no está
    /// conectado no inventa datos: los ciclos de ingesta se omiten, el OCR rechaza paquetes y la API de contratos
    /// responde error.
    /// </summary>
    public static bool SimulacionHabilitada(IConfiguration configuration, IHostEnvironment entorno)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(entorno);
        return bool.TryParse(configuration["Comparendos:Simulacion:Habilitada"], out var explicito)
            ? explicito
            : entorno.IsDevelopment() || entorno.IsEnvironment("Dev") || entorno.IsEnvironment("Local");
    }

    public static IServiceCollection AddModulosComparendos(this IServiceCollection services, IConfiguration configuration, IHostEnvironment entorno)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var simulacion = SimulacionHabilitada(configuration, entorno);
        services.AddScoped<Comun.Auditoria>();
        services.AddScoped<Comun.RegistroArchivos>();
        services.AddScoped<Catalogos.MapeadorOt>();
        services.AddScoped<Comparendos.Timeline>();
        services.AddScoped<Comparendos.AccesoComparendo>();
        services.AddScoped<Bandeja.ConsultaBandeja>();
        services.AddScoped<Configuracion.ResolutorRepresentante>();
        services.AddScoped<Configuracion.PermisosOt>();
        services.AddScoped<Plantillas.ContextoPlantilla>();
        services.AddScoped<Plantillas.ServicioPlantillas>();
        services.AddScoped<Contraventor.ServicioContraventor>();
        services.AddScoped<Portal.ServicioPortal>();
        services.AddScoped<Avisos.ServicioAvisos>();
        services.AddScoped<Avisos.FotoFotomulta>();
        services.AddSingleton<Avisos.IDescargaFoto, Avisos.DescargaFotoHttp>();
        services.AddHttpClient(Avisos.DescargaFotoHttp.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(Avisos.DescargaFotoHttp.Manejador);
        services.AddScoped<CambioContraventor.ServicioCambioContraventor>();
        services.AddScoped<DerechosPeticion.ServicioDp>();
        services.AddScoped<Correo.IReaccionCorreo, DerechosPeticion.ReaccionDp>();
        services.AddSingleton<DerechosPeticion.ProcesadorLotesDp>();
        services.AddScoped<Correo.IReaccionCorreo, CambioContraventor.ReaccionCambioContraventor>();
        services.AddScoped<Trabajos.IEjecutorTrabajo, Avisos.SchedulerAvisos>();
        services.AddScoped<Correo.IReaccionCorreo, Avisos.ReaccionAvisos>();
        services.AddLimitePortal();
        // API de contratos (HU #13712): por core-consultas si hay dirección; si no, simulada en DEV y nada fuera.
        if (configuration["Comparendos:Consultas:Address"] is { Length: > 0 })
            services.AddScoped<Contraventor.IApiContratos>(sp => new Contraventor.ApiContratosCoreConsultas(
                sp.GetRequiredService<Flit.Consultas.Grpc.V1.FuentesComparendosService.FuentesComparendosServiceClient>(),
                simulacion ? new Contraventor.ApiContratosSimulada() : null));
        else if (simulacion)
            services.AddScoped<Contraventor.IApiContratos, Contraventor.ApiContratosSimulada>();
        else
            services.AddScoped<Contraventor.IApiContratos, Contraventor.ApiContratosNoConectada>();
        services.AddScoped<Trabajos.IEjecutorTrabajo, Contraventor.ReintentoContraventor>();
        services.AddScoped<Bandeja.ExportacionBandeja>();

        // Reportes (HU #13498).
        services.AddScoped<Reportes.ServicioReportes>();
        services.AddScoped<Trabajos.IEjecutorTrabajo, Reportes.ReportesAutomaticos>();

        // OCR (HUs #13469–#13471): Anthropic si hay llave; si no, el modo simulado (local, DEV sin llave).
        services.AddScoped<Ocr.ServicioOcr>();
        services.AddSingleton<Ocr.ProcesadorOcr>();
        services.AddScoped<Ocr.ServicioEvaluacionPrompt>();
        services.AddSingleton<Ocr.EvaluadorPromptOcr>();
        services.Configure<Ocr.OpcionesAnthropic>(configuration.GetSection(Ocr.OpcionesAnthropic.Seccion));
        if (configuration[$"{Ocr.OpcionesAnthropic.Seccion}:ApiKey"] is { Length: > 0 })
            services.AddHttpClient<Ocr.IProveedorOcr, Ocr.ProveedorOcrAnthropic>(h => h.Timeout = TimeSpan.FromMinutes(3));
        else if (simulacion)
            services.AddSingleton<Ocr.IProveedorOcr, Ocr.ProveedorOcrSimulado>();
        else
            services.AddSingleton<Ocr.IProveedorOcr, Ocr.ProveedorOcrNoConfigurado>();

        // Ingesta (HU #13460, #13461). Las fuentes van por core-consultas (HU #13520); sin él, simulada en DEV y nada fuera.
        if (configuration["Comparendos:Consultas:Address"] is { Length: > 0 } consultas)
        {
            // Un NIT con cientos de comparendos en el SIMIT (Bancolombia) pasa de los 4 MB que gRPC acepta por defecto y la
            // clave fallaba con ResourceExhausted (prueba con SmartCloud real, 2026-10-19).
            services.AddFlitGrpcClient<Flit.Consultas.Grpc.V1.FuentesComparendosService.FuentesComparendosServiceClient>(
                    configuration, new Uri(consultas), "platform.consultas", o => o.Deadline = TimeSpan.FromSeconds(90))
                .ConfigureChannel(c => c.MaxReceiveMessageSize = 64 * 1024 * 1024);
            services.AddSingleton<Ingesta.Fuentes.IFuenteComparendos, Ingesta.Fuentes.FuenteCoreConsultas>();
        }
        else if (simulacion)
        {
            services.AddSingleton<Ingesta.Fuentes.IFuenteComparendos, Ingesta.Fuentes.FuenteSimulada>();
        }
        else
        {
            services.AddSingleton<Ingesta.Fuentes.IFuenteComparendos, Ingesta.Fuentes.FuenteNoConectada>();
        }

        // Activación controlada (HU #13521): Identidad dice si la empresa tiene el producto; sin Identidad, solo DEV.
        services.AddMemoryCache();
        if (configuration["Comparendos:Identidad:Address"] is { Length: > 0 } identidad)
        {
            services.AddFlitGrpcClient<Flit.Identidad.Grpc.V1.IdentidadService.IdentidadServiceClient>(
                configuration, new Uri(identidad), "platform.identidad.read", o => o.Deadline = TimeSpan.FromSeconds(10));
            services.AddSingleton<Plataforma.IProductosHabilitados, Plataforma.ProductosHabilitadosIdentidad>();
            services.AddScoped<Plataforma.IUsuariosEmpresa, Plataforma.UsuariosEmpresaIdentidad>();
            // Lista global de empresas para el parametrizador del SuperAdmin (B2-1, B2-4): su propio scope.
            services.AddFlitGrpcClient<Flit.Identidad.Grpc.V1.EmpresasService.EmpresasServiceClient>(
                configuration, new Uri(identidad), "platform.identidad.empresas", o => o.Deadline = TimeSpan.FromSeconds(10));
            services.AddScoped<Plataforma.IEmpresasPlataforma, Plataforma.EmpresasPlataformaIdentidad>();
        }
        else
        {
            services.AddSingleton<Plataforma.IProductosHabilitados>(new Plataforma.ProductosHabilitadosFijo(simulacion));
            services.AddSingleton<Plataforma.IUsuariosEmpresa, Plataforma.UsuariosEmpresaVacio>();
            services.AddSingleton<Plataforma.IEmpresasPlataforma, Plataforma.EmpresasPlataformaNoConectada>();
        }

        services.AddScoped<Ingesta.ElegibilidadConsultas>();
        services.AddScoped<Ingesta.Reconciliador>();
        services.AddScoped<Ingesta.CicloDeIngesta>();
        services.AddScoped<Trabajos.IEjecutorTrabajo>(sp => sp.GetRequiredService<Ingesta.CicloDeIngesta>());
        services.AddScoped<Ingesta.IAlertasIntegracion, Ingesta.AlertasIntegracion>();

        // Correo (HU #13484): por Notificaciones si hay dirección; si no, al log (local).
        services.AddScoped<Correo.ServicioCorreo>();
        services.AddSingleton<Correo.DespachadorCorreos>();
        if (configuration["Comparendos:Notificaciones:Address"] is { Length: > 0 } notificaciones)
        {
            services.AddFlitGrpcClient<Flit.Notificaciones.Grpc.V1.NotificacionesService.NotificacionesServiceClient>(
                configuration, new Uri(notificaciones), "platform.notificaciones.send", o => o.Deadline = TimeSpan.FromSeconds(60));
            services.AddScoped<Correo.IEnvioCorreo, Correo.EnvioPorNotificaciones>();
        }
        else
        {
            services.AddScoped<Correo.IEnvioCorreo, Correo.EnvioConsola>();
        }

        // Réplica del catálogo de organismos de tránsito (HU #13459, S-66): gRPC de catálogos de core-api. Sin dirección,
        // el trabajo no hace nada y queda la semilla.
        if (configuration["Comparendos:Tramites:Address"] is { Length: > 0 } tramites)
        {
            services.AddFlitGrpcClient<Flit.Tramites.Grpc.V1.CatalogosService.CatalogosServiceClient>(
                configuration, new Uri(tramites), "platform.tramites.catalogos", o => o.Deadline = TimeSpan.FromSeconds(60));
            services.AddScoped<Catalogos.ICatalogoOrganismosPlataforma, Catalogos.CatalogoOrganismosGrpc>();
        }
        else
        {
            services.AddScoped<Catalogos.ICatalogoOrganismosPlataforma, Catalogos.CatalogoOrganismosNoConectado>();
        }

        services.AddScoped<Catalogos.SincronizacionOrganismos>();
        services.AddScoped<Trabajos.IEjecutorTrabajo>(sp => sp.GetRequiredService<Catalogos.SincronizacionOrganismos>());

        services.AddSingleton<Trabajos.ColaTrabajos>();
        if (configuration.GetValue("Comparendos:Trabajos:Habilitados", true))
        {
            services.AddHostedService<Trabajos.ProgramadorTrabajos>();
            services.AddHostedService<Trabajos.ProcesadorTrabajos>();
            services.AddHostedService(sp => sp.GetRequiredService<Correo.DespachadorCorreos>());
            services.AddHostedService<Bandeja.ExportadorEnSegundoPlano>();
            services.AddHostedService(sp => sp.GetRequiredService<DerechosPeticion.ProcesadorLotesDp>());
            services.AddHostedService(sp => sp.GetRequiredService<Ocr.ProcesadorOcr>());
            services.AddHostedService(sp => sp.GetRequiredService<Ocr.EvaluadorPromptOcr>());
        }
        return services;
    }

    public static WebApplication MapModulosComparendos(this WebApplication app, string prefijo)
    {
        var api = app.MapGroup(prefijo);
        api.MapGet("/sesion", Sesion.Obtener).RequireAuthorization(Plataforma.AutorizacionExtensions.PoliticaUsuario);
        Comun.ArchivosEndpoints.Map(api);
        Comun.AuditoriaEndpoints.Map(api);
        Configuracion.ConsultasEmpresaEndpoints.Map(api);
        Monitoreo.MonitoreoEndpoints.Map(api);
        Catalogos.CatalogosEndpoints.Map(api);
        Ingesta.IngestaEndpoints.Map(api);
        Correo.CorreoEndpoints.Map(api);
        Ingesta.AlertasYLogEndpoints.Map(api);
        Bandeja.BandejaEndpoints.Map(api);
        Comparendos.DetalleEndpoints.Map(api);
        Configuracion.EmpresasEndpoints.Map(api);
        Configuracion.TipologiasEndpoints.Map(api);
        Configuracion.RepresentantesEndpoints.Map(api);
        Plantillas.PlantillasEndpoints.Map(api);
        Contraventor.ContraventorEndpoints.Map(api);
        Correo.MensajesEndpoints.Map(api);
        Portal.PortalEndpoints.Map(api);
        Avisos.AvisosEndpoints.Map(api);
        CambioContraventor.CambioContraventorEndpoints.Map(api);
        DerechosPeticion.DpEndpoints.Map(api);
        DerechosPeticion.LotesDpEndpoints.Map(api);
        Pagos.PagosEndpoints.Map(api);
        Ocr.OcrEndpoints.Map(api);
        Ocr.PromptsOcrEndpoints.Map(api);
        Reportes.ReportesEndpoints.Map(api);
        Calendario.CalendarioEndpoints.Map(api);
        return app;
    }
}
