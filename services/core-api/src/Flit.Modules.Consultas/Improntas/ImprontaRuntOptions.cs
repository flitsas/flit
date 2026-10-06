namespace Flit.Infrastructure.Improntas;

/// <summary>
/// Configuración del proveedor Kyverum RUNT (HU #10465), endpoint <c>improntas:generar</c>. Valores
/// desde <c>ImprontaRunt:*</c> (config) con fallback a env <c>KYVERUM_RUNT_*</c> — mismo patrón que
/// <c>KyverumOptions</c> (Kyverum Verify). <c>runt.kyverum.com</c> es un dominio DISTINTO de
/// <c>verify.kyverum.com</c> (mismo proveedor, otro producto). La API key NUNCA se loguea ni se
/// expone en DTOs.
/// </summary>
public sealed class ImprontaRuntOptions
{
    public const string SectionName = "ImprontaRunt";

    public string BaseUrl { get; set; } = "https://runt.kyverum.com";

    /// <summary>API key con scope <c>impronta:generar</c>. Solo el token, sin el prefijo del scheme.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string AuthScheme { get; set; } = "Bearer";

    public int TimeoutSeconds { get; set; } = 30;
}
