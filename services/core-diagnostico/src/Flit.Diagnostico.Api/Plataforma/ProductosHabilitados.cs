using Flit.Identidad.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Flit.Diagnostico.Api.Plataforma;

/// <summary>
/// ¿La compañía tiene Diagnóstico activo en la plataforma? (HU #13726, portado de Comparendos HU #13521). Lo responde
/// Identidad (<c>ObtenerProductosHabilitados</c>, regla fail-closed de la jerarquía). Si no se puede preguntar, la
/// respuesta es «no»: <see cref="GuardSuscripcion"/> niega la petición.
/// </summary>
public interface IProductosHabilitados
{
    Task<bool> DiagnosticoHabilitadoAsync(Guid tenant, CancellationToken ct);
}

/// <summary>
/// Por gRPC a Identidad, con caché corta por compañía (<see cref="Vigencia"/>: una suspensión se nota en ese plazo). Solo
/// se guarda en caché lo que Identidad respondió; un fallo al preguntar niega esa petición y no queda guardado.
/// </summary>
internal sealed partial class ProductosHabilitadosIdentidad(IdentidadService.IdentidadServiceClient cliente, IMemoryCache cache, ILogger<ProductosHabilitadosIdentidad> logger)
    : IProductosHabilitados
{
    public const string Producto = "diagnostico";

    /// <summary>Cuánto vale una respuesta de Identidad (activa o apagada) antes de volver a preguntar.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(1);

    public async Task<bool> DiagnosticoHabilitadoAsync(Guid tenant, CancellationToken ct)
    {
        if (cache.TryGetValue((nameof(ProductosHabilitadosIdentidad), tenant), out bool habilitado))
            return habilitado;
        try
        {
            var r = await cliente.ObtenerProductosHabilitadosAsync(new ObtenerProductosHabilitadosRequest(),
                new Metadata { { "x-flit-tenant-id", tenant.ToString() } }, cancellationToken: ct).ConfigureAwait(false);
            habilitado = r.Productos.Contains(Producto);
            cache.Set((nameof(ProductosHabilitadosIdentidad), tenant), habilitado, Vigencia);
            return habilitado;
        }
        catch (RpcException ex)
        {
            LogNoSePudo(logger, tenant, ex.StatusCode.ToString());
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Token de servicio, red o cualquier otro fallo al preguntar: fail-closed, igual que un RpcException.
            LogNoSePudo(logger, tenant, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo preguntar a Identidad por los productos de {Tenant} ({Motivo}): Diagnóstico se trata como no habilitado.")]
    private static partial void LogNoSePudo(ILogger logger, Guid tenant, string motivo);
}

/// <summary>
/// Sin Identidad configurada (<c>Diagnostico:Identidad:Address</c> vacío): ninguna compañía tiene Diagnóstico. Solo un
/// entorno local sin Identidad puede encenderlo para todas, y únicamente con
/// <c>Diagnostico:Identidad:SimularSuscripcionActiva=true</c> explícito (nunca por el nombre del ambiente: DEV, QA y PDN
/// corren como Development).
/// </summary>
internal sealed class ProductosSinIdentidad(bool simularActiva) : IProductosHabilitados
{
    public Task<bool> DiagnosticoHabilitadoAsync(Guid tenant, CancellationToken ct) => Task.FromResult(simularActiva);
}
