namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Punto de enganche del módulo de reportes con PII (HU #14579, BE-0, ADR-0088), en código separado de los reportes del lote (que siguen
/// enmascarados, SP-16): <c>Program</c> llama a <see cref="AddReportesCatalogo"/>, <c>MapModulosDiagnostico</c> a
/// <see cref="MapReportesCatalogo"/> (R0–R3, <c>/reportes*</c>) y <c>BackofficeEndpoints</c> a <see cref="MapReportesBackoffice"/>
/// (B0–B6, <c>/backoffice/reportes*</c>). BE-0 registra el registro de accesos (<see cref="RegistroAccesosReporte"/>, SP-8); las rutas
/// las llenan BE-2 (R0, R1), BE-3 (R2, R3) y BE-4 (B0–B6) sin tocar <c>Program.cs</c>. Reglas: R0–R3 con
/// <c>ReglasEstadisticasYReportes.RequiereVerReportes()</c> y, en R2/R3, además <c>RequirePermiso(Permisos.ReportesDescargar)</c>; B4–B6
/// en un grupo propio con <c>.SoloEmpresaHabilitada().ConEmpresaExplicita()</c> en ese orden; B0–B3 sin empresa explícita.
/// </summary>
internal static class ReportesRegistro
{
    /// <summary>Servicios del módulo: el registro de accesos (BE-0) y lo que sumen BE-2..BE-4.</summary>
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
        // BE-4: el reporte de consumo (solo backoffice) y las compañías de la petición.
        Backoffice.ReportesBackofficeEndpoints.AddServicios(services);
        return services;
    }

    /// <summary>R0–R3 bajo el grupo de la compañía (BE-2, BE-3).</summary>
    public static void MapReportesCatalogo(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        ReportesCatalogoEndpoints.Map(api);
    }

    /// <summary>B0–B6 bajo <c>/backoffice</c> (BE-4); recibe el grupo raíz de la API, no el de <c>ConEmpresaExplicita</c>.</summary>
    public static void MapReportesBackoffice(RouteGroupBuilder api) => Backoffice.ReportesBackofficeEndpoints.Map(api);
}
