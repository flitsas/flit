using Microsoft.Extensions.Options;

namespace Flit.Impuestos.Api.Modulos.Ejecucion;

/// <summary>
/// Freno global de la ejecución (plan §1.4, DA-5), uno por proceso:
/// <list type="bullet">
///   <item>Ritmo: como mucho <see cref="OpcionesEjecucion.TasaPorSegundo"/> POST por segundo hacia el API; <see cref="EsperarTurnoAsync"/>
///   espera el hueco siguiente.</item>
///   <item>Pausa: tras <see cref="OpcionesEjecucion.TransitoriosParaPausa"/> fallos transitorios seguidos (RUNT caído, «no salió»,
///   timeout o 5xx) queda <see cref="EnPausa"/> durante <see cref="OpcionesEjecucion.Pausa"/>; un éxito reinicia la cuenta. Mientras
///   tanto el consumidor no consulta el RUNT ni llama al API y deja el ítem para el fin de la pausa (<see cref="PausaHasta"/>).</item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// if (freno.EnPausa) { item.ProximoIntentoEn = freno.PausaHasta; return; }
/// await freno.EsperarTurnoAsync(ct);
/// </code>
/// </example>
public sealed class FrenoGlobal(IOptions<OpcionesEjecucion> opciones, TimeProvider reloj)
{
    private readonly Lock _candado = new();
    private int _transitorios;
    private DateTimeOffset? _pausaHasta;
    private DateTimeOffset _siguienteTurno = DateTimeOffset.MinValue;

    public bool EnPausa => PausaHasta is not null;

    /// <summary>Fin de la pausa en curso; null si no hay.</summary>
    public DateTimeOffset? PausaHasta
    {
        get
        {
            lock (_candado)
                return _pausaHasta is { } hasta && hasta > reloj.GetUtcNow() ? hasta : null;
        }
    }

    public void RegistrarTransitorio()
    {
        lock (_candado)
        {
            _transitorios++;
            if (_transitorios >= Math.Max(1, opciones.Value.TransitoriosParaPausa))
            {
                _pausaHasta = reloj.GetUtcNow() + opciones.Value.Pausa;
                _transitorios = 0;
            }
        }
    }

    public void RegistrarExito()
    {
        lock (_candado)
            _transitorios = 0;
    }

    /// <summary>Espera, si hace falta, el siguiente hueco del ritmo global.</summary>
    public async Task EsperarTurnoAsync(CancellationToken ct)
    {
        TimeSpan espera;
        lock (_candado)
        {
            var ahora = reloj.GetUtcNow();
            var intervalo = TimeSpan.FromSeconds(1 / Math.Max(0.001, opciones.Value.TasaPorSegundo));
            var turno = _siguienteTurno > ahora ? _siguienteTurno : ahora;
            _siguienteTurno = turno + intervalo;
            espera = turno - ahora;
        }

        if (espera > TimeSpan.Zero)
            await Task.Delay(espera, ct).ConfigureAwait(false);
    }
}
