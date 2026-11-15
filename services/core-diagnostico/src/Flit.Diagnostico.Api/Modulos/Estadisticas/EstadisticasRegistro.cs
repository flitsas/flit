namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Punto de enganche del módulo de estadísticas (HU #14579, BE-0, ADR-0088): <c>Program</c> llama a <see cref="AddEstadisticas"/>,
/// <c>MapModulosDiagnostico</c> a <see cref="MapEstadisticas"/> (E1, <c>GET /estadisticas</c>, filtro de tenant normal) y
/// <c>BackofficeEndpoints</c> a <see cref="MapEstadisticasBackoffice"/> (E2 <c>/backoffice/estadisticas/global</c> y E3
/// <c>/backoffice/estadisticas?empresa=</c>). Nacen vacíos para que BE-1 los llene sin tocar <c>Program.cs</c> ni los grupos
/// existentes. Reglas que BE-1 debe usar: E1 con <c>RequirePermiso(Permisos.DiagnosticosVer)</c>; E2 y E3 con
/// <c>ReglasEstadisticasYReportes.RequiereEstadisticasGlobales()</c>; E3 en un grupo propio con
/// <c>.SoloEmpresaHabilitada().ConEmpresaExplicita()</c> en ese orden; E2 sin empresa explícita. Las consultas globales viven en
/// <c>Modulos/Backoffice</c> (SP-4).
/// </summary>
internal static class EstadisticasRegistro
{
    /// <summary>Servicios del módulo (BE-1).</summary>
    public static IServiceCollection AddEstadisticas(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }

    /// <summary>E1 bajo el grupo de la compañía (BE-1).</summary>
    public static void MapEstadisticas(RouteGroupBuilder api) => ArgumentNullException.ThrowIfNull(api);

    /// <summary>E2 y E3 bajo <c>/backoffice</c> (BE-1); recibe el grupo raíz de la API, no el de <c>ConEmpresaExplicita</c>.</summary>
    public static void MapEstadisticasBackoffice(RouteGroupBuilder api) => ArgumentNullException.ThrowIfNull(api);
}
