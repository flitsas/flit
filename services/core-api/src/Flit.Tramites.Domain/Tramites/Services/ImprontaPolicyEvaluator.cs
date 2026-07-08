namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>Decisión de la política de validación por IA de improntas (HU #10522, RF40).</summary>
public enum ImprontaPolicyDecision
{
    /// <summary>La coincidencia alcanza el umbral: avanza sin observaciones.</summary>
    Ok,

    /// <summary>Por debajo del umbral pero la política es "advertir": muestra aviso y permite avanzar.</summary>
    Warn,

    /// <summary>Por debajo del umbral y la política es "bloquear": exige re-captura.</summary>
    Block,
}

/// <summary>
/// Evalúa la política de validación por IA de improntas de forma pura y determinista (RF40).
/// Por defecto la política es <b>advertir</b> (no bloquea la radicación): si el porcentaje de
/// coincidencia queda por debajo del umbral, devuelve <see cref="ImprontaPolicyDecision.Warn"/>.
/// </summary>
public static class ImprontaPolicyEvaluator
{
    /// <param name="matchPercent">Coincidencia en [0,1] (o [0,100]; se normaliza).</param>
    /// <param name="threshold">Umbral en la misma escala que <paramref name="matchPercent"/>.</param>
    /// <param name="blockBelowThreshold">Si <c>true</c>, bajo el umbral bloquea; si <c>false</c>, solo advierte.</param>
    public static ImprontaPolicyDecision Evaluate(double matchPercent, double threshold, bool blockBelowThreshold)
    {
        var match = Normalize(matchPercent);
        var umbral = Normalize(threshold);

        if (match >= umbral)
            return ImprontaPolicyDecision.Ok;

        return blockBelowThreshold ? ImprontaPolicyDecision.Block : ImprontaPolicyDecision.Warn;
    }

    private static double Normalize(double value) => value > 1.0 ? value / 100.0 : value;
}
