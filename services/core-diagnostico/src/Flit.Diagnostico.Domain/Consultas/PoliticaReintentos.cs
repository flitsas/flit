namespace Flit.Diagnostico.Domain.Consultas;

/// <summary>Qué hacer con una celda que terminó en error.</summary>
/// <param name="Reintentar">Verdadero si vuelve a la cola (PENDIENTE) en lugar de quedar en ERROR.</param>
/// <param name="DisponibleDesde">Desde cuándo se puede volver a tomar; null si no se reintenta.</param>
/// <param name="IntentosAuto">Valor de <c>intentos_auto</c> que queda en la celda.</param>
public sealed record DecisionReintento(bool Reintentar, DateTimeOffset? DisponibleDesde, int IntentosAuto);

/// <summary>
/// Reintentos automáticos de una celda (HU #13737, F03-H6, D-12; ajuste 2026-11-11 Punto 3; HU #14752, D5 = a): solo de errores
/// transitorios (<see cref="ClasificadorErrores"/>), con backoff exponencial y azar: el reintento n espera
/// <c>base × 2^(n−1) × (1 + factor × (2·aleatorio − 1))</c>, con la base <see cref="Espera"/> (por defecto
/// <see cref="EsperaPorDefecto"/> = 1 min, configurable en [<see cref="EsperaMinima"/>, <see cref="EsperaMaxima"/>]) y el factor
/// por defecto <see cref="FactorAleatorioPorDefecto"/> (1, 2 y 4 min ±20 %). Un <see cref="CodigoLimite"/> espera al menos
/// <see cref="EsperaMinimaLimite"/> (60 s por defecto: Intempo pide «intenta nuevamente en 1 minuto»). Hasta
/// <see cref="MaxReintentos"/>, que es configuración global de la aplicación en [0, 3] (por defecto 3). <c>intentos_auto</c> cuenta
/// los reintentos ya programados: un transitorio con <c>intentos_auto</c> menor que el máximo vuelve a la cola; un definitivo, o
/// los reintentos agotados, dejan la celda en ERROR reprocesable (reproceso por celda, F03-H8, o reintento manual de las pegadas,
/// <see cref="CriterioPegada"/>). El instante y el aleatorio los da quien llama (reloj y azar inyectables): la política no duerme,
/// no mira el reloj del sistema ni sortea.
/// </summary>
/// <example>
/// <code>
/// var politica = new PoliticaReintentos(3, TimeSpan.FromMinutes(1), 0.2, TimeSpan.FromSeconds(60));
/// var decision = politica.Decidir(resultado.TipoError, celda.IntentosAuto, reloj.GetUtcNow(), Random.Shared.NextDouble());
/// </code>
/// </example>
public sealed class PoliticaReintentos
{
    public const int Minimo = 0;
    public const int Maximo = 3;
    public const int PorDefecto = 3;

    /// <summary>Desvío aleatorio por defecto de cada espera (±20 %, HU #14752).</summary>
    public const double FactorAleatorioPorDefecto = 0.2;

    /// <summary>Desvío aleatorio máximo permitido (±50 %).</summary>
    public const double FactorAleatorioMaximo = 0.5;

    /// <summary>Código del error de límite del proveedor, que espera al menos <see cref="EsperaMinimaLimite"/>.</summary>
    public const string CodigoLimite = "LIMITE_PROVEEDOR";

    /// <summary>Espera BASE del backoff por defecto (HU #14752: 1 min; antes, espera fija de 2 min).</summary>
    public static readonly TimeSpan EsperaPorDefecto = TimeSpan.FromMinutes(1);

    /// <summary>Espera mínima permitida.</summary>
    public static readonly TimeSpan EsperaMinima = TimeSpan.FromSeconds(1);

    /// <summary>Espera máxima permitida (muy por debajo del TTL de 24 h de la cola de resultados).</summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(1);

    /// <summary>Espera mínima por defecto de un <see cref="CodigoLimite"/> (60 s).</summary>
    public static readonly TimeSpan EsperaMinimaLimitePorDefecto = TimeSpan.FromSeconds(60);

    /// <summary>Tope del mínimo configurable del límite (10 min).</summary>
    public static readonly TimeSpan EsperaMinimaLimiteMaxima = TimeSpan.FromMinutes(10);

    public PoliticaReintentos(int maxReintentos)
        : this(maxReintentos, EsperaPorDefecto)
    {
    }

    /// <summary>Sin azar (factor 0) y con el mínimo del límite por defecto: los usos que ya existían siguen deterministas.</summary>
    public PoliticaReintentos(int maxReintentos, TimeSpan espera)
        : this(maxReintentos, espera, 0, EsperaMinimaLimitePorDefecto)
    {
    }

    public PoliticaReintentos(int maxReintentos, TimeSpan esperaBase, double factorAleatorio, TimeSpan esperaMinimaLimite)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxReintentos, Minimo);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxReintentos, Maximo);
        ArgumentOutOfRangeException.ThrowIfLessThan(esperaBase, EsperaMinima);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(esperaBase, EsperaMaxima);
        if (double.IsNaN(factorAleatorio) || factorAleatorio < 0 || factorAleatorio > FactorAleatorioMaximo)
            throw new ArgumentOutOfRangeException(nameof(factorAleatorio), factorAleatorio, $"El factor aleatorio va en [0, {FactorAleatorioMaximo}].");
        ArgumentOutOfRangeException.ThrowIfLessThan(esperaMinimaLimite, EsperaMinima);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(esperaMinimaLimite, EsperaMinimaLimiteMaxima);
        MaxReintentos = maxReintentos;
        Espera = esperaBase;
        FactorAleatorio = factorAleatorio;
        EsperaMinimaLimite = esperaMinimaLimite;
    }

    /// <summary>Reintentos automáticos permitidos por celda.</summary>
    public int MaxReintentos { get; }

    /// <summary>Espera base del backoff (la del primer reintento, sin azar).</summary>
    public TimeSpan Espera { get; }

    /// <summary>Desvío aleatorio de cada espera, en [0, <see cref="FactorAleatorioMaximo"/>].</summary>
    public double FactorAleatorio { get; }

    /// <summary>Espera mínima de un <see cref="CodigoLimite"/>.</summary>
    public TimeSpan EsperaMinimaLimite { get; }

    /// <summary>
    /// La espera más larga que puede dar la política: la del último reintento con el azar al máximo, o el mínimo del límite si es
    /// mayor (con <see cref="MaxReintentos"/> = 0 cuenta un reintento). Fija el número de colas de espera y el umbral de «pegada».
    /// </summary>
    public TimeSpan EsperaMaximaEfectiva
    {
        get
        {
            var ultimo = Math.Max(MaxReintentos, 1);
            var espera = TimeSpan.FromMilliseconds(Math.Round(Espera.TotalMilliseconds * Math.Pow(2, ultimo - 1) * (1 + FactorAleatorio)));
            return espera > EsperaMinimaLimite ? espera : EsperaMinimaLimite;
        }
    }

    /// <param name="tipoError">Código del error con que terminó la ejecución.</param>
    /// <param name="intentosAuto">Reintentos ya programados para la celda.</param>
    /// <param name="ahora">Instante del fin de la ejecución (reloj inyectado).</param>
    /// <param name="aleatorio">Valor en [0, 1] (azar inyectado); 0,5 no desvía la espera.</param>
    public DecisionReintento Decidir(string? tipoError, int intentosAuto, DateTimeOffset ahora, double aleatorio = 0.5)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(intentosAuto);
        if (double.IsNaN(aleatorio) || aleatorio < 0 || aleatorio > 1)
            throw new ArgumentOutOfRangeException(nameof(aleatorio), aleatorio, "El aleatorio va en [0, 1].");
        if (ClasificadorErrores.Clasificar(tipoError) != ClaseError.Transitorio || intentosAuto >= MaxReintentos)
            return new DecisionReintento(false, null, intentosAuto);

        var n = intentosAuto + 1;
        var milisegundos = Espera.TotalMilliseconds * Math.Pow(2, n - 1) * (1 + (FactorAleatorio * ((2 * aleatorio) - 1)));
        var espera = TimeSpan.FromMilliseconds(Math.Round(milisegundos));
        if (string.Equals(tipoError, CodigoLimite, StringComparison.Ordinal) && espera < EsperaMinimaLimite)
            espera = EsperaMinimaLimite;
        return new DecisionReintento(true, ahora + espera, n);
    }
}
