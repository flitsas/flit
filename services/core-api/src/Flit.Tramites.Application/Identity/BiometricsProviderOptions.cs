using Flit.Tramites.Domain.Entities;

namespace Flit.Tramites.Application.Identity;

/// <summary>
/// Feature flag del proveedor de validación de identidad (HU #10233, AC4). Default <c>mock</c>:
/// preserva el flujo Slice 6 (scorer determinista de 3 fotos) y no rompe la regresión existente.
/// Se conmuta a <c>kyverum</c> vía <c>Biometrics:Provider</c> (config) / <c>BIOMETRICS_PROVIDER</c> (env).
/// </summary>
public sealed class BiometricsProviderOptions
{
    public const string SectionName = "Biometrics";

    public string Provider { get; set; } = BiometricProviders.Mock;

    /// <summary>true cuando el proveedor configurado es Kyverum (comparación case-insensitive).</summary>
    public bool IsKyverum =>
        string.Equals(Provider, BiometricProviders.Kyverum, StringComparison.OrdinalIgnoreCase);
}
