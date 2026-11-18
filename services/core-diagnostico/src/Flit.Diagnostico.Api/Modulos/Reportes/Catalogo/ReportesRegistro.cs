namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Punto de enganche del módulo de reportes con PII (HU #14579, BE-0, ADR-0088), en código separado de los reportes del lote (que siguen
/// enmascarados, SP-16): <c>Program</c> llama a <see cref="AddReportesCatalogo"/>, <c>MapModulosDiagnostico</c> a
/// <see cref="MapReportesCatalogo"/> (R0–R3, <c>/reportes*</c>). BE-0 registra el registro de accesos (<see cref="RegistroAccesosReporte"/>,
/// SP-8). Reglas: R0–R3 con <c>ReglasEstadisticasYReportes.RequiereVerReportes()</c> y, en R2/R3, además
/// <c>RequirePermiso(Permisos.ReportesDescargar)</c>. El catálogo no conoce al backoffice (CR-BE-9): los reportes del backoffice (B0–B6,
/// el reporte <c>consumo</c> y sus servicios) los registran <c>Program</c> y <c>BackofficeEndpoints</c> desde <c>Modulos/Backoffice</c>.
/// </summary>
internal static class ReportesRegistro
{
    /// <summary>Servicios del módulo: el registro de accesos (BE-0), el motor, los cuatro reportes de la compañía y la exportación.</summary>
    public static IServiceCollection AddReportesCatalogo(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddScoped<RegistroAccesosReporte>();
        // BE-2: motor genérico, los cuatro reportes de la compañía y el descifrado en memoria (reutilizables por el backoffice, BE-4).
        services.AddOptions<OpcionesReportes>()
            .Bind(configuration.GetSection(OpcionesReportes.Seccion))
            .Validate(o => o.Validos(), $"{OpcionesReportes.Seccion}: los máximos, las exportaciones simultáneas y el tiempo de comando deben ser positivos.")
            .ValidateOnStart();
        services.AddScoped<NombresTramite>();
        services.AddScoped<IReporte, ReporteVehiculos>();
        services.AddScoped<IReporte, ReporteConsultas>();
        services.AddScoped<IReporte, ReporteSaneamientos>();
        services.AddScoped<IReporte, ReporteDerivaciones>();
        services.AddScoped<DescifradoDocumentos>();
        services.AddScoped<MotorReportes>();
        // BE-3: exportación a Excel y PDF con el freno de concurrencia del proceso (singleton, compartido con el backoffice).
        services.AddSingleton<FrenoExportaciones>();
        services.AddScoped<ExportadorReportes>();
        return services;
    }

    /// <summary>R0–R3 bajo el grupo de la compañía (BE-2, BE-3).</summary>
    public static void MapReportesCatalogo(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ReportesCatalogoEndpoints.Map(api);
    }
}
