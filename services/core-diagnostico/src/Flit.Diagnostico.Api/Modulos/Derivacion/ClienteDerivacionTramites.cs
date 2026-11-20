using Flit.Diagnostico.Domain.Derivacion;
using Flit.Tramites.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>
/// Resultado de pedir un borrador a Trámites (Punto 4, diseño §3): <see cref="Creado"/> (también si ya existía, idempotencia por
/// <c>(empresa, external_ref)</c>), <see cref="Rechazado"/> (regla de negocio, no reintentable: el ítem queda OMITIDO) o
/// <see cref="NoDisponible"/> (status gRPC; <c>Transitorio</c> si esperar puede arreglarlo).
/// </summary>
public abstract record ResultadoBorrador
{
    public sealed record Creado(Guid TramiteId, string Numero, bool YaExistia) : ResultadoBorrador;

    public sealed record Rechazado(string Codigo, string Motivo) : ResultadoBorrador;

    public sealed record NoDisponible(bool Transitorio) : ResultadoBorrador;
}

/// <summary>Un trámite que Trámites ya tiene para un vehículo (<c>external_ref</c>): id y número.</summary>
public sealed record TramiteReferencia(Guid TramiteId, string Numero);

/// <summary>
/// El rpc <c>flit.tramites.v1.DerivacionTramitesService.CrearBorradorDesdeDiagnostico</c> visto desde Diagnóstico. Nunca lanza por
/// un error de Trámites: lo traduce a <see cref="ResultadoBorrador"/>. El individual y (incremento 3) el consumidor del masivo lo
/// usan igual.
/// </summary>
public interface IClienteDerivacionTramites
{
    /// <summary>Pide el borrador con la empresa del lote en <c>x-flit-tenant-id</c>.</summary>
    Task<ResultadoBorrador> CrearAsync(Guid empresa, CrearBorradorDesdeDiagnosticoRequest solicitud, CancellationToken ct);

    /// <summary>
    /// Trámites que ya existen para esos vehículos (rpc <c>ObtenerTramitesPorReferencia</c>, como mucho
    /// <see cref="ClienteDerivacionTramitesGrpc.MaxReferencias"/> por llamada; el llamador parte la lista). Null si Trámites no respondió:
    /// el reconciliador no decide nada sin saberlo.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, TramiteReferencia>?> ObtenerPorReferenciaAsync(Guid empresa, IReadOnlyCollection<Guid> vehiculos, CancellationToken ct);
}

/// <summary>
/// Traducción de la respuesta y de los status del rpc (diseño §3, tabla de códigos): <c>creado</c> es éxito con o sin
/// <c>ya_existia</c> (un id que no es uuid no lo es); los nueve rechazos de <see cref="MotivoDerivacion.RechazosTramites"/> pasan
/// con su código (otro código, <see cref="MotivoDerivacion.RechazoTramites"/>); RESOURCE_EXHAUSTED, UNAVAILABLE, INTERNAL y
/// DEADLINE_EXCEEDED son transitorios; cualquier otro status (PERMISSION_DENIED, UNAUTHENTICATED, UNIMPLEMENTED con la derivación
/// apagada en core-api, INVALID_ARGUMENT…) es de configuración: TRAMITES_NO_DISPONIBLE no transitorio.
/// </summary>
public static class TraductorDerivacion
{
    public static ResultadoBorrador Desde(CrearBorradorDesdeDiagnosticoResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        switch (respuesta.ResultadoCase)
        {
            case CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Creado
                when Guid.TryParse(respuesta.Creado.ProcedureInstanceId, out var id) && id != Guid.Empty:
                return new ResultadoBorrador.Creado(id, respuesta.Creado.Numero.Trim(), respuesta.Creado.YaExistia);
            case CrearBorradorDesdeDiagnosticoResponse.ResultadoOneofCase.Rechazo:
                var conocido = MotivoDerivacion.RechazosTramites.Contains(respuesta.Rechazo.Codigo, StringComparer.Ordinal);
                var codigo = conocido ? respuesta.Rechazo.Codigo : MotivoDerivacion.RechazoTramites;
                var motivo = string.IsNullOrWhiteSpace(respuesta.Rechazo.Motivo) ? MotivoDerivacion.Mensaje(codigo) : respuesta.Rechazo.Motivo.Trim();
                return new ResultadoBorrador.Rechazado(codigo, motivo);
            default:
                return new ResultadoBorrador.NoDisponible(Transitorio: false);
        }
    }

    public static ResultadoBorrador Desde(RpcException error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ResultadoBorrador.NoDisponible(Transitorio: EsTransitorio(error.StatusCode));
    }

    public static bool EsTransitorio(StatusCode estado) =>
        estado is StatusCode.ResourceExhausted or StatusCode.Unavailable or StatusCode.Internal or StatusCode.DeadlineExceeded;
}

/// <summary>
/// Cliente gRPC real (token de servicio de svc-diagnostico, <c>aud=tramites</c>, scope <c>platform.tramites.derivacion</c>,
/// registrado por <see cref="DerivacionEndpoints.AddDerivacion"/>). Pone la empresa del lote en <c>x-flit-tenant-id</c> (no la de la
/// petición: el consumidor del masivo no tiene petición) y el deadline de <see cref="OpcionesDerivacion.PlazoRpcSegundos"/>. Sin
/// reintento propio: el SDK reintenta UNAVAILABLE y el masivo tiene sus colas de reintento. Logs: solo ids, status y duración.
/// </summary>
public sealed partial class ClienteDerivacionTramitesGrpc(
    DerivacionTramitesService.DerivacionTramitesServiceClient cliente,
    IOptions<OpcionesDerivacion> opciones,
    TimeProvider reloj,
    ILogger<ClienteDerivacionTramitesGrpc> logger) : IClienteDerivacionTramites
{
    public const string MetadataEmpresa = "x-flit-tenant-id";

    /// <summary>Tope de referencias por llamada de <c>ObtenerTramitesPorReferencia</c> (diseño §3).</summary>
    public const int MaxReferencias = 500;

    public async Task<IReadOnlyDictionary<Guid, TramiteReferencia>?> ObtenerPorReferenciaAsync(Guid empresa, IReadOnlyCollection<Guid> vehiculos, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(vehiculos);
        var encontrados = new Dictionary<Guid, TramiteReferencia>();
        foreach (var bloque in vehiculos.Distinct().Chunk(MaxReferencias))
        {
            var solicitud = new ObtenerTramitesPorReferenciaRequest();
            solicitud.ExternalRefs.AddRange(bloque.Select(v => v.ToString()));
            try
            {
                var respuesta = await cliente.ObtenerTramitesPorReferenciaAsync(
                        solicitud,
                        new Metadata { { MetadataEmpresa, empresa.ToString() } },
                        reloj.GetUtcNow().UtcDateTime.AddSeconds(opciones.Value.PlazoRpcSegundos),
                        ct)
                    .ConfigureAwait(false);
                foreach (var t in respuesta.Tramites)
                {
                    if (Guid.TryParse(t.ExternalRef, out var vehiculo) && Guid.TryParse(t.ProcedureInstanceId, out var id) && id != Guid.Empty)
                        encontrados[vehiculo] = new TramiteReferencia(id, t.Numero.Trim());
                }
            }
            catch (RpcException ex) when (!ct.IsCancellationRequested)
            {
                LogReferencias(logger, bloque.Length, ex.StatusCode);
                return null;
            }
        }

        return encontrados;
    }

    public async Task<ResultadoBorrador> CrearAsync(Guid empresa, CrearBorradorDesdeDiagnosticoRequest solicitud, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var inicio = reloj.GetTimestamp();
        try
        {
            var respuesta = await cliente.CrearBorradorDesdeDiagnosticoAsync(
                    solicitud,
                    new Metadata { { MetadataEmpresa, empresa.ToString() } },
                    reloj.GetUtcNow().UtcDateTime.AddSeconds(opciones.Value.PlazoRpcSegundos),
                    ct)
                .ConfigureAwait(false);
            var resultado = TraductorDerivacion.Desde(respuesta);
            var codigo = resultado is ResultadoBorrador.Rechazado r ? r.Codigo : null;
            var milisegundos = reloj.GetElapsedTime(inicio).TotalMilliseconds;
            LogResultado(logger, solicitud.IntentoId, resultado.GetType().Name, codigo, milisegundos);
            return resultado;
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            var resultado = TraductorDerivacion.Desde(ex);
            var milisegundos = reloj.GetElapsedTime(inicio).TotalMilliseconds;
            if (resultado is ResultadoBorrador.NoDisponible { Transitorio: false })
                LogConfiguracion(logger, solicitud.IntentoId, ex.StatusCode);
            else
                LogTransitorio(logger, solicitud.IntentoId, ex.StatusCode, milisegundos);
            return resultado;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "CrearBorradorDesdeDiagnostico del ítem {ItemId}: {Resultado} {Codigo} en {Milisegundos} ms.")]
    private static partial void LogResultado(ILogger logger, string itemId, string resultado, string? codigo, double milisegundos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CrearBorradorDesdeDiagnostico del ítem {ItemId} falló con {Estado} en {Milisegundos} ms (transitorio).")]
    private static partial void LogTransitorio(ILogger logger, string itemId, StatusCode estado, double milisegundos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ObtenerTramitesPorReferencia de {Cantidad} referencias falló con {Estado}; el reconciliador lo intenta en la siguiente vuelta.")]
    private static partial void LogReferencias(ILogger logger, int cantidad, StatusCode estado);

    [LoggerMessage(Level = LogLevel.Error, Message = "CrearBorradorDesdeDiagnostico del ítem {ItemId} rechazó a svc-diagnostico con {Estado}: revisar el scope platform.tramites.derivacion y Tramites:Derivacion:Habilitada de core-api.")]
    private static partial void LogConfiguracion(ILogger logger, string itemId, StatusCode estado);
}

/// <summary>Sin <c>Diagnostico:Tramites:Address</c>: no hay con quién hablar; responde no disponible (no transitorio).</summary>
public sealed class ClienteDerivacionNoConectado : IClienteDerivacionTramites
{
    public Task<ResultadoBorrador> CrearAsync(Guid empresa, CrearBorradorDesdeDiagnosticoRequest solicitud, CancellationToken ct) =>
        Task.FromResult<ResultadoBorrador>(new ResultadoBorrador.NoDisponible(Transitorio: false));

    public Task<IReadOnlyDictionary<Guid, TramiteReferencia>?> ObtenerPorReferenciaAsync(Guid empresa, IReadOnlyCollection<Guid> vehiculos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, TramiteReferencia>?>(null);
}
