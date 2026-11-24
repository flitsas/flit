using System.Collections.Concurrent;
using Flit.Identidad.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Consultas.Api.Impuesto.Credencial;

/// <summary>
/// Cliente de <c>CredencialesProductoService</c> con caché por empresa (diseño §1.8).
/// </summary>
internal sealed partial class FuenteCredencialImpuestoPlataforma(
    CredencialesProductoService.CredencialesProductoServiceClient cliente,
    IMemoryCache cache,
    TimeProvider time,
    ILogger<FuenteCredencialImpuestoPlataforma> logger) : IFuenteCredencialImpuesto
{
    public const string Scope = "platform.identidad.credenciales.impuesto";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TopeLocal = TimeSpan.FromSeconds(600);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _vuelos = new(StringComparer.Ordinal);

    public async Task<CredencialImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct)
    {
        var clave = Clave(tenantId);
        if (!forzar && cache.TryGetValue(clave, out CredencialImpuesto? cacheada) && cacheada is not null)
            return cacheada;

        var semaforo = _vuelos.GetOrAdd(clave, _ => new SemaphoreSlim(1, 1));
        await semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forzar && cache.TryGetValue(clave, out cacheada) && cacheada is not null)
                return cacheada;

            var headers = new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } };
            var opciones = new CallOptions(headers, deadline: time.GetUtcNow().UtcDateTime.Add(Deadline), cancellationToken: ct);
            ObtenerCredencialImpuestoResponse respuesta;
            try
            {
                respuesta = await cliente.ObtenerCredencialImpuestoAsync(new ObtenerCredencialImpuestoRequest(), opciones)
                    .ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
            {
                throw new FalloCredencialImpuestoException(new FalloProveedor(ClaseFalloProveedor.Red), "impuesto_red");
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.Unauthenticated or StatusCode.PermissionDenied)
            {
                CredencialDenegada(logger, ex.StatusCode.ToString());
                throw new FalloCredencialImpuestoException(new FalloProveedor(ClaseFalloProveedor.Credenciales), "impuesto_autenticacion");
            }

            var credencial = Mapear(respuesta);
            var ttl = TimeSpan.FromSeconds(Math.Clamp(respuesta.TtlSegundos <= 0 ? 60 : respuesta.TtlSegundos, 1, (int)TopeLocal.TotalSeconds));
            cache.Set(clave, credencial, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
            return credencial;
        }
        finally
        {
            semaforo.Release();
        }
    }

    private static CredencialImpuesto Mapear(ObtenerCredencialImpuestoResponse r)
    {
        if (r.Disponible)
            return new CredencialImpuesto(true, r.ApiKey, r.Version, MotivoSinCredencial.Ninguno);
        var motivo = r.Motivo switch
        {
            MotivoCredencialNoDisponible.ProductoApagado => MotivoSinCredencial.ProductoApagado,
            _ => MotivoSinCredencial.SinConfigurar,
        };
        return new CredencialImpuesto(false, null, r.Version, motivo);
    }

    private static string Clave(Guid tenantId) => "impuesto:credencial:" + tenantId.ToString("N");

    [LoggerMessage(EventId = 7440, Level = LogLevel.Error, Message = "Credencial de impuesto denegada por Identidad ({Status}).")]
    private static partial void CredencialDenegada(ILogger logger, string status);
}
