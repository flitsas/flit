namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>Qué hacer con una celda que terminó en error.</summary>
/// <param name="Reintentar">Verdadero si vuelve a la cola (PENDIENTE) en lugar de quedar en ERROR.</param>
/// <param name="DisponibleDesde">Desde cuándo se puede volver a tomar; null si no se reintenta.</param>
/// <param name="IntentosAuto">Valor de <c>intentos_auto</c> que queda en la celda.</param>
public sealed record DecisionReintento(bool Reintentar, DateTimeOffset? DisponibleDesde, int IntentosAuto);

/// <summary>
/// Reintentos automáticos de una celda (HU #13737, F03-H6, D-12): solo de errores transitorios
/// (<see cref="ClasificadorErrores"/>), con backoff exponencial <c>5 s × 2^(n−1)</c> (n = número del reintento: 5, 10 y 20
/// segundos) y hasta <see cref="MaxReintentos"/>, que es configuración global de la aplicación en [0, 3] (por defecto 1).
/// <c>intentos_auto</c> cuenta los reintentos ya programados: un transitorio con <c>intentos_auto</c> menor que el máximo vuelve a
/// la cola; un definitivo, o los reintentos agotados, dejan la celda en ERROR reprocesable (el reproceso es F03-H8). El instante
/// lo da quien llama (reloj inyectable): la política no duerme ni mira el reloj del sistema.
/// </summary>
/// <example>
/// <code>
/// var decision = new PoliticaReintentos(opciones.MaxReintentos).Decidir(resultado.TipoError, celda.IntentosAuto, reloj.GetUtcNow());
/// </code>
/// </example>
public sealed class PoliticaReintentos
{
    public const int Minimo = 0;
    public const int Maximo = 3;
    public const int PorDefecto = 1;

    private static readonly TimeSpan Base = TimeSpan.FromSeconds(5);

    public PoliticaReintentos(int maxReintentos)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReintentos, Minimo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxReintentos, Maximo);
        MaxReintentos = maxReintentos;
    }

    /// <summary>Reintentos automáticos permitidos por celda.</summary>
    public int MaxReintentos { get; }

    /// <summary>Espera antes del reintento número <paramref name="reintento"/> (1, 2, 3…): 5 s × 2^(n−1).</summary>
    public static TimeSpan Espera(int reintento)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reintento, 1);
        return Base * Math.Pow(2, reintento - 1);
    }

    /// <param name="tipoError">Código del error con que terminó la ejecución.</param>
    /// <param name="intentosAuto">Reintentos ya programados para la celda.</param>
    /// <param name="ahora">Instante del fin de la ejecución (reloj inyectado).</param>
    public DecisionReintento Decidir(string? tipoError, int intentosAuto, DateTimeOffset ahora)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intentosAuto);
        if (ClasificadorErrores.Clasificar(tipoError) != ClaseError.Transitorio || intentosAuto >= MaxReintentos)
            return new DecisionReintento(false, null, intentosAuto);

        var reintento = intentosAuto + 1;
        return new DecisionReintento(true, ahora + Espera(reintento), reintento);
    }
}
