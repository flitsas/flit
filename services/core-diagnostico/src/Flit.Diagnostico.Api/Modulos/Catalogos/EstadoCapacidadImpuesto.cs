using Flit.Diagnostico.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Catalogos;

/// <summary>
/// Si la réplica <c>diagnostico.departamentos_impuesto</c> tiene al menos un departamento soportado (HU #13861; lo consume
/// F09-H4 para decidir si la fuente del impuesto está disponible, Q5=a). Singleton: lo carga desde la base la primera vuelta
/// del trabajador (también sin adaptador conectado) y lo actualiza <see cref="SincronizacionDepartamentosImpuesto"/> tras cada
/// sincronización. Antes de la primera carga es falso (cierre seguro). Dos recargas concurrentes no dejan un valor viejo: gana
/// la última que empezó a leer.
/// </summary>
/// <example>
/// <code>
/// if (!estado.HaySoportados) throw ReglaException.FuenteNoDisponible(...);
/// </code>
/// </example>
public sealed class EstadoCapacidadImpuesto
{
    private readonly Lock _cerrojo = new();
    private long _pedidas;
    private long _aplicada;
    private bool _haySoportados;

    /// <summary>Verdadero si hay al menos un departamento con <c>soportado=true</c> en la última lectura de la réplica.</summary>
    public bool HaySoportados
    {
        get
        {
            lock (_cerrojo)
                return _haySoportados;
        }
    }

    /// <summary>Si ya se leyó la réplica al menos una vez.</summary>
    public bool Cargado
    {
        get
        {
            lock (_cerrojo)
                return _aplicada > 0;
        }
    }

    /// <summary>Vuelve a leer la réplica (tabla global, sin empresa).</summary>
    public async Task RecargarAsync(DiagnosticoDb db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var pedida = Interlocked.Increment(ref _pedidas);
        var hay = await db.Set<DepartamentoImpuesto>().AsNoTracking().AnyAsync(d => d.Soportado, ct).ConfigureAwait(false);
        Aplicar(pedida, hay);
    }

    /// <summary>
    /// Fija el valor sin leer la base (HU #13863): lo usan las pruebas de la matriz de disponibilidad. Cuenta como una recarga
    /// más (gana sobre las lecturas que empezaron antes).
    /// </summary>
    internal void Fijar(bool haySoportados) => Aplicar(Interlocked.Increment(ref _pedidas), haySoportados);

    private void Aplicar(long pedida, bool hay)
    {
        lock (_cerrojo)
        {
            if (pedida <= _aplicada)
                return;
            _aplicada = pedida;
            _haySoportados = hay;
        }
    }
}
