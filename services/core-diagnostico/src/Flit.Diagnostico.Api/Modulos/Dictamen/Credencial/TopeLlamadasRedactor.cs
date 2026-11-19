namespace Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;

/// <summary>
/// SP-17: tope local de llamadas por minuto al proveedor (ventana deslizante de 60 s). Con <c>Origen=plataforma</c> la llave es la
/// general de core-api (la misma de OCR y DR. FLIT): el tope evita que Diagnóstico agote su cuota. Pasado el tope, la llamada no sale
/// y queda <see cref="TipoFalloRedactor.RateLimit"/> con la espera hasta que se libere un cupo (la cola la reintenta).
/// </summary>
public sealed class TopeLlamadasRedactor
{
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(1);

    private readonly int _max;
    private readonly TimeProvider _reloj;
    private readonly Queue<DateTimeOffset> _llamadas = new();
    private readonly Lock _cerrojo = new();

    public TopeLlamadasRedactor(int maxPorMinuto, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        _max = maxPorMinuto > 0 ? maxPorMinuto : OpcionesIa.MaxLlamadasPorMinutoPorDefecto;
        _reloj = reloj;
    }

    private TopeLlamadasRedactor()
    {
        _max = int.MaxValue;
        _reloj = TimeProvider.System;
    }

    /// <summary>Sin tope: <c>Origen=local</c>, con la llave propia del desarrollador (comportamiento de siempre).</summary>
    public static TopeLlamadasRedactor SinTope() => new();

    /// <summary>True y cuenta la llamada si hay cupo; si no, <paramref name="espera"/> es lo que falta para que salga la más vieja.</summary>
    public bool IntentarTomar(out TimeSpan espera)
    {
        espera = TimeSpan.Zero;
        if (_max == int.MaxValue)
            return true;
        lock (_cerrojo)
        {
            var ahora = _reloj.GetUtcNow();
            while (_llamadas.Count > 0 && ahora - _llamadas.Peek() >= Ventana)
                _llamadas.Dequeue();
            if (_llamadas.Count < _max)
            {
                _llamadas.Enqueue(ahora);
                return true;
            }

            espera = _llamadas.Peek() + Ventana - ahora;
            return false;
        }
    }
}
