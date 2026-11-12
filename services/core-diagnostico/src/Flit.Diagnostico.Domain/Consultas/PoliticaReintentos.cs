namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>Qué hacer con una celda que terminó en error.</summary>
/// <param name="Reintentar">Verdadero si vuelve a la cola (PENDIENTE) en lugar de quedar en ERROR.</param>
/// <param name="DisponibleDesde">Desde cuándo se puede volver a tomar; null si no se reintenta.</param>
/// <param name="IntentosAuto">Valor de <c>intentos_auto</c> que queda en la celda.</param>
public sealed record DecisionReintento(bool Reintentar, DateTimeOffset? DisponibleDesde, int IntentosAuto);

/// <summary>
/// Reintentos automáticos de una celda (HU #13737, F03-H6, D-12; ajuste 2026-11-11 Punto 3, decisión 3.1): solo de errores
/// transitorios (<see cref="ClasificadorErrores"/>), con una espera FIJA entre intentos (<see cref="Espera"/>, por defecto
/// <see cref="EsperaPorDefecto"/> = 2 min, configurable en [<see cref="EsperaMinima"/>, <see cref="EsperaMaxima"/>]) y hasta
/// <see cref="MaxReintentos"/>, que es configuración global de la aplicación en [0, 3] (por defecto 3). <c>intentos_auto</c> cuenta
/// los reintentos ya programados: un transitorio con <c>intentos_auto</c> menor que el máximo vuelve a la cola; un definitivo, o
/// los reintentos agotados, dejan la celda en ERROR reprocesable (reproceso por celda, F03-H8, o reintento manual de las pegadas,
/// <see cref="CriterioPegada"/>). El instante lo da quien llama (reloj inyectable): la política no duerme ni mira el reloj del
/// sistema.
/// </summary>
/// <example>
/// <code>
/// var decision = new PoliticaReintentos(opciones.MaxReintentos, opciones.EsperaReintento).Decidir(resultado.TipoError, celda.IntentosAuto, reloj.GetUtcNow());
/// </code>
/// </example>
public sealed class PoliticaReintentos
{
    public const int Minimo = 0;
    public const int Maximo = 3;
    public const int PorDefecto = 3;

    /// <summary>Espera entre intentos por defecto (Punto 3, decisión 3.1).</summary>
    public static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromMinutes(2);

    /// <summary>Espera mínima permitida.</summary>
    public static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(1);

    /// <summary>Espera máxima permitida (muy por debajo del TTL de 24 h de la cola de resultados).</summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(1);

    public PoliticaReintentos(int maxReintentos)
        : this(maxReintentos, EsperaPorDefecto)
    {
    }

    public PoliticaReintentos(int maxReintentos, TimeSpan espera)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReintentos, Minimo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxReintentos, Maximo);
        ArgumentOutOfRangeException.ThrowIfLessThan(espera, EsperaMinima);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(espera, EsperaMaxima);
        MaxReintentos = maxReintentos;
        Espera = espera;
    }

    /// <summary>Reintentos automáticos permitidos por celda.</summary>
    public int MaxReintentos { get; }

    /// <summary>Espera fija antes de cada reintento.</summary>
    public TimeSpan Espera { get; }

    /// <param name="tipoError">Código del error con que terminó la ejecución.</param>
    /// <param name="intentosAuto">Reintentos ya programados para la celda.</param>
    /// <param name="ahora">Instante del fin de la ejecución (reloj inyectado).</param>
    public DecisionReintento Decidir(string? tipoError, int intentosAuto, DateTimeOffset ahora)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intentosAuto);
        if (ClasificadorErrores.Clasificar(tipoError) != ClaseError.Transitorio || intentosAuto >= MaxReintentos)
            return new DecisionReintento(false, null, intentosAuto);

        return new DecisionReintento(true, ahora + Espera, intentosAuto + 1);
    }
}
