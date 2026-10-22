using Flit.Identidad.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Comparendos.Api.Plataforma;

/// <summary>
/// ¿La empresa tiene Comparendos habilitado en la plataforma? (HU #13521). Lo responde Identidad
/// (<c>ObtenerProductosHabilitados</c>, regla fail-closed de la jerarquía). Si no se puede preguntar, la respuesta es
/// «no»: un trabajo en segundo plano nunca consulta ni notifica a una empresa sin el producto.
/// </summary>
public interface IProductosHabilitados
{
    Task<bool> ComparendosHabilitadoAsync(Guid tenant, CancellationToken ct);
}

/// <summary>Por gRPC a Identidad, con caché de 5 minutos por empresa (un apagado se nota en ese plazo).</summary>
internal sealed partial class ProductosHabilitadosIdentidad(IdentidadService.IdentidadServiceClient cliente, IMemoryCache cache, ILogger<ProductosHabilitadosIdentidad> logger)
    : IProductosHabilitados
{
    public const string Producto = "comparendos";

    public async Task<bool> ComparendosHabilitadoAsync(Guid tenant, CancellationToken ct)
    {
        if (cache.TryGetValue(("productos", tenant), out bool habilitado))
            return habilitado;
        try
        {
            var r = await cliente.ObtenerProductosHabilitadosAsync(new ObtenerProductosHabilitadosRequest(),
                new Metadata { { "x-flit-tenant-id", tenant.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            habilitado = r.Productos.Contains(Producto);
            cache.Set(("productos", tenant), habilitado, TimeSpan.FromMinutes(5));
            return habilitado;
        }
        catch (RpcException ex)
        {
            LogNoSePudo(logger, tenant, ex.StatusCode);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo preguntar a Identidad por los productos de {Tenant} ({Codigo}): se trata como no habilitado.")]
    private static partial void LogNoSePudo(ILogger logger, Guid tenant, StatusCode codigo);
}

/// <summary>Sin Identidad configurada: en DEV (modo simulado) todas habilitadas; en los ambientes, ninguna.</summary>
internal sealed class ProductosHabilitadosFijo(bool todas) : IProductosHabilitados
{
    public Task<bool> ComparendosHabilitadoAsync(Guid tenant, CancellationToken ct) => Task.FromResult(todas);
}
