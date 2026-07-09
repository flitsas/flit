namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Política de validación por IA de improntas (HU #10522, RF40). Sección de configuración
/// <c>ImprontaValidation</c>. Por defecto <b>advertir</b> (no bloquea) con umbral 0.8.
/// </summary>
public sealed class ImprontaValidationPolicyOptions
{
    public const string SectionName = "ImprontaValidation";

    /// <summary>Umbral de coincidencia (0..1). Por debajo se aplica la política.</summary>
    public double MatchThreshold { get; set; } = 0.8;

    /// <summary>Si <c>true</c>, bajo el umbral bloquea el avance; si <c>false</c> (por defecto), solo advierte.</summary>
    public bool BlockBelowThreshold { get; set; }
}
