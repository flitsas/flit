using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Freno de concurrencia de las exportaciones de reportes (HU #14579, BE-3; SP-12): como mucho
/// <see cref="OpcionesReportes.ExportacionesPorUsuario"/> por usuario (<c>sub</c>) y <see cref="OpcionesReportes.ExportacionesPorProceso"/>
/// en el proceso, con <see cref="ConcurrencyLimiter"/> sin cola. Singleton: compañía y backoffice (BE-4) comparten el mismo freno. El
/// llamador libera el permiso al terminar (dispose).
/// </summary>
internal sealed class FrenoExportaciones : IDisposable
{
    private readonly PartitionedRateLimiter<Guid> _porUsuario;
    private readonly ConcurrencyLimiter _proceso;

    public FrenoExportaciones(IOptions<OpcionesReportes> opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var o = opciones.Value;
        _porUsuario = PartitionedRateLimiter.Create<Guid, Guid>(usuario => RateLimitPartition.GetConcurrencyLimiter(usuario,
            _ => new ConcurrencyLimiterOptions { PermitLimit = o.ExportacionesPorUsuario, QueueLimit = 0 }));
        _proceso = new ConcurrencyLimiter(new ConcurrencyLimiterOptions { PermitLimit = o.ExportacionesPorProceso, QueueLimit = 0 });
    }

    /// <summary>
    /// Un permiso para exportar, o null con el <paramref name="motivo"/> (<c>usuario</c> o <c>proceso</c>) si ya hay demasiadas en curso.
    /// </summary>
    public IDisposable? Entrar(Guid usuario, out string? motivo)
    {
        var propio = _porUsuario.AttemptAcquire(usuario);
        if (!propio.IsAcquired)
        {
            propio.Dispose();
            motivo = "usuario";
            return null;
        }

        var global = _proceso.AttemptAcquire();
        if (!global.IsAcquired)
        {
            global.Dispose();
            propio.Dispose();
            motivo = "proceso";
            return null;
        }

        motivo = null;
        return new Permiso(propio, global);
    }

    public void Dispose()
    {
        _porUsuario.Dispose();
        _proceso.Dispose();
    }

    private sealed class Permiso(RateLimitLease propio, RateLimitLease global) : IDisposable
    {
        public void Dispose()
        {
            global.Dispose();
            propio.Dispose();
        }
    }
}
