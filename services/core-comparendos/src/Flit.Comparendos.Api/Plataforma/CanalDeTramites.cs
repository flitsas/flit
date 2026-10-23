using Flit.Comparendos.Domain.Modelo;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>
/// HU #13882: el canal de correo de una empresa es el «Enrutamiento de notificaciones» que eligió en Trámites, no uno
/// propio de Comparendos. «API Renting cliente» = la API de correo de la empresa con los textos de Renting; «Colas FLIT»
/// (o sin política) = el correo de FLIT con los de FLIT. La lista de Identidad se guarda 5 minutos: un cambio en Trámites
/// se nota en ese plazo.
/// </summary>
public sealed partial class CanalDeTramites(IEmpresasPlataforma plataforma, IMemoryCache cache, ILogger<CanalDeTramites> logger)
{
    public const string ClaveCache = "canales-tramites";

    /// <summary>
    /// El canal de la empresa, o null si no se puede saber (sin Identidad, Identidad no responde o no informa el canal):
    /// entonces se conserva el último conocido, para no frenar la operación por una caída de Identidad.
    /// </summary>
    public async Task<CanalCorreo?> DeAsync(Guid tenantId, CancellationToken ct)
    {
        if (!cache.TryGetValue(ClaveCache, out IReadOnlyList<EmpresaPlataforma>? empresas))
        {
            try
            {
                empresas = await plataforma.ListarAsync(ct).ConfigureAwait(false);
            }
            catch (RpcException ex)
            {
                LogNoSePudo(logger, ex.StatusCode);
                empresas = null;
            }

            // Un fallo se recuerda poco tiempo, para volver a preguntar pronto.
            cache.Set(ClaveCache, empresas, empresas is null ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(5));
        }

        if (empresas is null)
            return null;
        // Una empresa que Identidad no lista no tiene el producto encendido; si llegara a enviar algo, va por FLIT.
        return empresas.FirstOrDefault(e => e.Id == tenantId) is { } empresa ? empresa.Canal : CanalCorreo.FlitSmtp;
    }

    /// <summary>El juego de plantillas que va con cada canal: la API de la empresa es Renting.</summary>
    public static ConjuntoPlantillas ConjuntoDe(CanalCorreo canal) => canal == CanalCorreo.EmpresaApi ? ConjuntoPlantillas.Renting : ConjuntoPlantillas.Flit;

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer el canal de correo de Trámites en Identidad ({Codigo}): se conserva el último conocido.")]
    private static partial void LogNoSePudo(ILogger logger, StatusCode codigo);
}
