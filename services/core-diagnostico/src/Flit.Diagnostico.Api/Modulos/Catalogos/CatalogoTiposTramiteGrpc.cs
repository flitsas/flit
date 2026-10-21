using Flit.Diagnostico.Domain.Catalogos;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;
using CatalogosService = Flit.Tramites.Grpc.V1.CatalogosService;
using ListarTiposTramiteRequest = Flit.Tramites.Grpc.V1.ListarTiposTramiteRequest;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Catálogo de tipos de trámite por el gRPC de catálogos de core-api (<c>flit.tramites.v1.CatalogosService</c>, rpc
/// <c>ListarTiposTramite</c>, HU #13731), con el token de servicio de svc-diagnostico (<c>aud=tramites</c>, scope
/// <c>platform.tramites.catalogos</c>), como Comparendos con los organismos (HU #13742). Filtra por código con la lista
/// blanca <see cref="ClaveHabilitados"/> y guarda <see cref="Vigencia"/> la última respuesta correcta: con copia vigente
/// no llama al rpc. Unavailable o DeadlineExceeded se reintentan 2 veces (200 ms y 600 ms, 3 s por llamada); si sigue
/// fallando, o ante cualquier otro error, lanza <see cref="CatalogoTiposTramiteNoDisponibleException"/> (sin lista fija ni
/// copia vencida). PermissionDenied y Unauthenticated no se reintentan y se registran como error de configuración.
/// </summary>
public sealed partial class CatalogoTiposTramiteGrpc(
    CatalogosService.CatalogosServiceClient cliente,
    IMemoryCache cache,
    IConfiguration configuration,
    TimeProvider reloj,
    ILogger<CatalogoTiposTramiteGrpc> logger) : ICatalogoTiposTramite
{
    public const string ClaveHabilitados = "Diagnostico:TiposTramite:Habilitados";

    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan PlazoPorLlamada = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan[] Esperas = [TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)];

    private static readonly object ClaveCache = new();

    public async Task<IReadOnlyList<TipoTramite>> ListarAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(ClaveCache, out IReadOnlyList<TipoTramite>? vigentes) && vigentes is not null)
            return vigentes;

        var publicados = await ListarPublicadosAsync(ct).ConfigureAwait(false);
        var habilitados = FiltroTiposTramite.Habilitados(publicados, configuration.GetSection(ClaveHabilitados).Get<string[]>() ?? []);
        cache.Set(ClaveCache, habilitados, Vigencia);
        return habilitados;
    }

    private async Task<IReadOnlyList<TipoTramite>> ListarPublicadosAsync(CancellationToken ct)
    {
        for (var intento = 0; ; intento++)
        {
            try
            {
                var r = await cliente.ListarTiposTramiteAsync(
                        new ListarTiposTramiteRequest(), deadline: reloj.GetUtcNow().UtcDateTime.Add(PlazoPorLlamada), cancellationToken: ct)
                    .ConfigureAwait(false);
                return [.. r.Tipos.Where(t => t.Codigo.Length > 0).Select(t => new TipoTramite(t.Codigo.Trim(), t.Nombre.Trim(), t.Familia.Trim(), []))];
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded && intento < Esperas.Length)
            {
                LogReintento(logger, ex.StatusCode, intento + 1);
                await Task.Delay(Esperas[intento], reloj, ct).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.PermissionDenied or StatusCode.Unauthenticated)
            {
                LogConfiguracion(logger, ex.StatusCode);
                throw new CatalogoTiposTramiteNoDisponibleException("Trámites rechazó al cliente de servicio de Diagnóstico.", ex);
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            {
                LogNoDisponible(logger, ex.StatusCode, intento + 1);
                throw new CatalogoTiposTramiteNoDisponibleException("Trámites no respondió el catálogo de tipos de trámite.", ex);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ListarTiposTramite falló con {Estado}; reintento {Reintento}.")]
    private static partial void LogReintento(ILogger logger, StatusCode estado, int reintento);

    [LoggerMessage(Level = LogLevel.Error, Message = "ListarTiposTramite rechazó a svc-diagnostico con {Estado}: revisar el scope platform.tramites.catalogos y Catalogos:TiposTramite:ClientesPermitidos de core-api.")]
    private static partial void LogConfiguracion(ILogger logger, StatusCode estado);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catálogo de tipos de trámite no disponible: {Estado} tras {Intentos} intento(s).")]
    private static partial void LogNoDisponible(ILogger logger, StatusCode estado, int intentos);
}

/// <summary>Sin <c>Diagnostico:Tramites:Address</c>: no hay con quién hablar y el catálogo responde no disponible (503).</summary>
public sealed class CatalogoTiposTramiteNoConectado : ICatalogoTiposTramite
{
    public Task<IReadOnlyList<TipoTramite>> ListarAsync(CancellationToken ct) =>
        throw new CatalogoTiposTramiteNoDisponibleException("No hay dirección del gRPC de catálogos de Trámites (Diagnostico:Tramites:Address).");
}
