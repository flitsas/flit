using System.Collections.Concurrent;
using Flit.Identidad.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Consultas.Api.Impuesto.Avisos;

/// <summary>
/// Secreto del webhook por gRPC (<c>CredencialesProductoService.ObtenerSecretoWebhookImpuesto</c>, HU #14711) con caché por empresa
/// (patrón <see cref="Credencial.FuenteCredencialImpuestoPlataforma"/>): <see cref="TtlConSecreto"/> con secreto y
/// <see cref="TtlSinSecreto"/> sin él. Con <c>forzar</c> (la firma no coincidió con la versión cacheada) relee, pero como mucho una
/// vez cada <see cref="EsperaMinimaForzada"/> por empresa, para que una ráfaga de avisos inválidos no se vuelva una ráfaga contra
/// Identidad. Identidad caída o denegando, o cualquier otra falla del cliente que no sea <see cref="RpcException"/> (L2, log solo con
/// el tipo): <see cref="FalloSecretoWebhookImpuestoException"/>. Nunca registra los secretos.
/// </summary>
/// <example>
/// <code>
/// var secreto = await fuente.ObtenerAsync(tenantId, forzar: false, ct);
/// if (secreto.Configurado) VerificadorFirmaImpuesto.Verificar(secreto.Vigentes(ahora), ts, firma, cuerpo, ahora);
/// </code>
/// </example>
internal sealed partial class FuenteSecretoWebhookImpuesto(
    ClienteSecretoWebhookImpuesto cliente,
    IMemoryCache cache,
    TimeProvider time,
    ILogger<FuenteSecretoWebhookImpuesto> logger) : IFuenteSecretoWebhookImpuesto
{
    public const string Scope = "platform.identidad.credenciales.impuesto.webhook";
    public static readonly TimeSpan TtlConSecreto = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan TtlSinSecreto = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan EsperaMinimaForzada = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _vuelos = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _ultimaLectura = new();

    public async Task<SecretoWebhookImpuesto> ObtenerAsync(Guid tenantId, bool forzar, CancellationToken ct)
    {
        var clave = Clave(tenantId);
        if (Cacheado(clave, tenantId, forzar) is { } cacheado)
            return cacheado;

        var semaforo = _vuelos.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await semaforo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Cacheado(clave, tenantId, forzar) is { } otraVez)
                return otraVez;

            var headers = new Metadata { { PlatformServiceCallInterceptor.TenantMetadata, tenantId.ToString() } };
            var opciones = new CallOptions(headers, deadline: time.GetUtcNow().UtcDateTime.Add(Deadline), cancellationToken: ct);
            ObtenerSecretoWebhookImpuestoResponse respuesta;
            try
            {
                respuesta = await cliente.ObtenerSecretoWebhookImpuestoAsync(new ObtenerSecretoWebhookImpuestoRequest(), opciones)
                    .ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode is not StatusCode.Cancelled || !ct.IsCancellationRequested)
            {
                SecretoNoDisponible(logger, ex.StatusCode.ToString());
                throw new FalloSecretoWebhookImpuestoException(ex.StatusCode.ToString());
            }
            catch (Exception ex) when (ex is not RpcException && !(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                // L2: canal roto, DNS, TLS o cualquier otra falla del cliente: para el receptor es lo mismo que Identidad caída (503).
                FallaInesperada(logger, ex.GetType().Name);
                throw new FalloSecretoWebhookImpuestoException("falla_inesperada");
            }

            var secreto = Mapear(respuesta);
            _ultimaLectura[tenantId] = time.GetUtcNow();
            cache.Set(clave, secreto, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = secreto.Configurado ? TtlConSecreto : TtlSinSecreto,
            });
            return secreto;
        }
        finally
        {
            semaforo.Release();
        }
    }

    /// <summary>
    /// Lo cacheado si sirve: sin <paramref name="forzar"/>, si existe; con <paramref name="forzar"/>, solo si la última lectura fue hace
    /// menos de <see cref="EsperaMinimaForzada"/>.
    /// </summary>
    private SecretoWebhookImpuesto? Cacheado(string clave, Guid tenantId, bool forzar)
    {
        if (!cache.TryGetValue(clave, out SecretoWebhookImpuesto? cacheado) || cacheado is null)
            return null;
        if (!forzar)
            return cacheado;
        return _ultimaLectura.TryGetValue(tenantId, out var ultima) && time.GetUtcNow() - ultima < EsperaMinimaForzada ? cacheado : null;
    }

    private static SecretoWebhookImpuesto Mapear(ObtenerSecretoWebhookImpuestoResponse r)
    {
        if (!r.Configurado || string.IsNullOrEmpty(r.Actual))
            return SecretoWebhookImpuesto.SinConfigurar with { Version = r.Version };
        var conAnterior = !string.IsNullOrEmpty(r.Anterior) && r.AnteriorVenceEn is not null;
        return new SecretoWebhookImpuesto(
            true,
            r.Actual,
            conAnterior ? r.Anterior : null,
            conAnterior ? r.AnteriorVenceEn!.ToDateTimeOffset() : null,
            r.Version);
    }

    private static string Clave(Guid tenantId) => "impuesto:webhook:" + tenantId.ToString("N");

    [LoggerMessage(EventId = 7441, Level = LogLevel.Warning, Message = "Secreto del webhook del impuesto no disponible en Identidad ({Status}).")]
    private static partial void SecretoNoDisponible(ILogger logger, string status);

    [LoggerMessage(EventId = 7442, Level = LogLevel.Error, Message = "Secreto del webhook del impuesto: falla inesperada al pedirlo a Identidad ({Tipo}).")]
    private static partial void FallaInesperada(ILogger logger, string tipo);
}
