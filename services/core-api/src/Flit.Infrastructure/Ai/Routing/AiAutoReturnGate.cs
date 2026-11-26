using System.Collections.Concurrent;

namespace Flit.Infrastructure.Ai.Routing;

/// <summary>
/// Cada cuánto el router vuelve a probar el proveedor principal de una herramienta que quedó en respaldo
/// tras un failover (<c>auto_return_to_primary</c>). El intento va dentro de una petición real: si el
/// principal sigue caído, esa petición paga su error (un 401 tarda milisegundos, un corte de red hasta el
/// timeout del proveedor) y sigue con el respaldo. Por eso se hace como mucho una vez por intervalo, por
/// herramienta y por réplica.
/// <para>Estado en memoria del proceso (singleton): con dos réplicas, cada una prueba por su cuenta. Es lo
/// deseado — no hace falta coordinarlas y la base solo registra el regreso de la que llegue primero.</para>
/// </summary>
internal sealed class AiAutoReturnGate(TimeSpan intervalo, Func<DateTimeOffset>? reloj = null)
{
    /// <summary>Intervalo por defecto entre pruebas del principal.</summary>
    public static readonly TimeSpan IntervaloPorDefecto = TimeSpan.FromMinutes(5);

    private readonly Func<DateTimeOffset> _reloj = reloj ?? (() => DateTimeOffset.UtcNow);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _ultimaPrueba = new(StringComparer.Ordinal);

    public TimeSpan Intervalo { get; } = intervalo;

    /// <summary>Reinicia la cuenta: tras un failover, el principal no se vuelve a probar hasta un intervalo después.</summary>
    public void Marcar(string toolCode) => _ultimaPrueba[toolCode] = _reloj();

    /// <summary>
    /// <c>true</c> si toca probar el principal de <paramref name="toolCode"/> ahora; en ese caso deja
    /// marcada la prueba para que las peticiones concurrentes del mismo intervalo no la repitan.
    /// </summary>
    public bool TryEnter(string toolCode)
    {
        var ahora = _reloj();
        while (true)
        {
            if (!_ultimaPrueba.TryGetValue(toolCode, out var ultima))
            {
                if (_ultimaPrueba.TryAdd(toolCode, ahora))
                    return true;
                continue;
            }

            if (ahora - ultima < Intervalo)
                return false;

            if (_ultimaPrueba.TryUpdate(toolCode, ahora, ultima))
                return true;
        }
    }
}
