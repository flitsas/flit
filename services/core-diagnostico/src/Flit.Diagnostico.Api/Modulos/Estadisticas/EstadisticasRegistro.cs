using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Estadisticas;

/// <summary>
/// Punto de enganche del módulo de estadísticas (HU #14579, ADR-0088): <c>MapModulosDiagnostico</c> llama a <see cref="MapEstadisticas"/>
/// (E1, <c>GET /estadisticas</c>, filtro de tenant normal, <see cref="EstadisticasEndpoints"/>). La calculadora es estática, así que el
/// módulo no registra servicios. E2 y E3 los mapea <c>BackofficeEndpoints</c> desde <c>Modulos/Backoffice</c>, que depende de este
/// módulo y no al revés (CR-BE-9); las consultas globales viven allí (SP-4). Timeout de comando acotado (SP-11) con la clave compartida
/// <see cref="ClaveTimeout"/> (30 s por defecto).
/// </summary>
internal static class EstadisticasRegistro
{
    public const string ClaveTimeout = "Diagnostico:Reportes:TimeoutSegundos";
    public const int TimeoutPorDefecto = 30;

    /// <summary>E1 bajo el grupo de la compañía.</summary>
    public static void MapEstadisticas(RouteGroupBuilder api) => EstadisticasEndpoints.Map(api);

    /// <summary>Timeout de comando de las consultas de estadísticas (SP-11).</summary>
    public static void AcotarTiempo(DiagnosticoDb db, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(configuracion);
        var segundos = configuracion.GetValue<int?>(ClaveTimeout) is { } valor && valor > 0 ? valor : TimeoutPorDefecto;
        db.Database.SetCommandTimeout(segundos);
    }
}
