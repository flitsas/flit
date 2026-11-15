using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Punto de enganche del módulo de estadísticas (HU #14579, ADR-0088): <c>Program</c> llama a <see cref="AddEstadisticas"/>,
/// <c>MapModulosDiagnostico</c> a <see cref="MapEstadisticas"/> (E1, <c>GET /estadisticas</c>, filtro de tenant normal,
/// <see cref="EstadisticasEndpoints"/>) y <c>BackofficeEndpoints</c> a <see cref="MapEstadisticasBackoffice"/> (E2
/// <c>/backoffice/estadisticas/global</c> y E3 <c>/backoffice/estadisticas?empresa=</c>, <c>Backoffice.EstadisticasBackofficeEndpoints</c>).
/// Las consultas globales viven en <c>Modulos/Backoffice</c> (SP-4). Timeout de comando acotado (SP-11) con la clave compartida
/// <see cref="ClaveTimeout"/> (30 s por defecto).
/// </summary>
internal static class EstadisticasRegistro
{
    public const string ClaveTimeout = "Diagnostico:Reportes:TimeoutSegundos";
    public const int TimeoutPorDefecto = 30;

    /// <summary>Servicios del módulo: la calculadora es estática y no registra nada.</summary>
    public static IServiceCollection AddEstadisticas(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services;
    }

    /// <summary>E1 bajo el grupo de la compañía.</summary>
    public static void MapEstadisticas(RouteGroupBuilder api) => EstadisticasEndpoints.Map(api);

    /// <summary>E2 y E3 bajo <c>/backoffice</c>; recibe el grupo raíz de la API, no el de <c>ConEmpresaExplicita</c>.</summary>
    public static void MapEstadisticasBackoffice(RouteGroupBuilder api) => Backoffice.EstadisticasBackofficeEndpoints.Map(api);

    /// <summary>Timeout de comando de las consultas de estadísticas (SP-11).</summary>
    public static void AcotarTiempo(DiagnosticoDb db, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(configuracion);
        var segundos = configuracion.GetValue<int?>(ClaveTimeout) is { } valor && valor > 0 ? valor : TimeoutPorDefecto;
        db.Database.SetCommandTimeout(segundos);
    }
}
