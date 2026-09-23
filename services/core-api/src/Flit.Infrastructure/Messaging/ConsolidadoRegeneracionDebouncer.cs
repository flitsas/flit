namespace Flit.Infrastructure.Messaging;

/// <summary>
/// HU #12795 (AC2) — debounce + coalescing por clave (tenant, trámite, documento), sin reloj propio:
/// el tiempo lo pasa el llamador (<see cref="TimeProvider"/> del worker), así que se prueba con tiempo
/// falso y sin esperas reales.
///
/// <para>Debounce "de cola": cada solicitud de una clave ya pendiente reinicia su vencimiento a
/// <c>ahora + ventana</c>, sin superar <c>primera + esperaMaxima</c>. Varias solicitudes dentro de la
/// ventana producen UN solo vencimiento.</para>
///
/// <para>No es thread-safe: lo usa únicamente el bucle del worker (un solo lector del canal).</para>
/// </summary>
internal sealed class ConsolidadoRegeneracionDebouncer
{
    private readonly TimeSpan _ventana;
    private readonly TimeSpan _esperaMaxima;
    private readonly Dictionary<SolicitudRegeneracion, (DateTimeOffset Primera, DateTimeOffset Vence)> _pendientes = [];

    public ConsolidadoRegeneracionDebouncer(TimeSpan ventana, TimeSpan esperaMaxima)
    {
        _ventana = ventana < TimeSpan.Zero ? TimeSpan.Zero : ventana;
        _esperaMaxima = esperaMaxima < _ventana ? _ventana : esperaMaxima;
    }

    public int Pendientes => _pendientes.Count;

    public void Registrar(SolicitudRegeneracion solicitud, DateTimeOffset ahora)
    {
        if (_pendientes.TryGetValue(solicitud, out var actual))
        {
            var tope = actual.Primera + _esperaMaxima;
            var vence = ahora + _ventana;
            _pendientes[solicitud] = (actual.Primera, vence < tope ? vence : tope);
            return;
        }

        _pendientes[solicitud] = (ahora, ahora + _ventana);
    }

    /// <summary>Retira y devuelve las claves vencidas, en orden de vencimiento.</summary>
    public IReadOnlyList<SolicitudRegeneracion> TomarVencidas(DateTimeOffset ahora)
    {
        if (_pendientes.Count == 0)
            return [];

        var vencidas = _pendientes
            .Where(p => p.Value.Vence <= ahora)
            .OrderBy(p => p.Value.Vence)
            .Select(p => p.Key)
            .ToList();

        foreach (var clave in vencidas)
            _pendientes.Remove(clave);

        return vencidas;
    }

    /// <summary>Cuánto falta para el próximo vencimiento; <c>null</c> si no hay nada pendiente.</summary>
    public TimeSpan? TiempoHastaProximo(DateTimeOffset ahora)
    {
        if (_pendientes.Count == 0)
            return null;

        var proximo = _pendientes.Values.Min(v => v.Vence);
        var falta = proximo - ahora;
        return falta > TimeSpan.Zero ? falta : TimeSpan.Zero;
    }
}
