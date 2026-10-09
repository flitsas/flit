namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Configuración del proveedor Verifik (RUNT Colombia). Los valores se cargan
/// desde variables de entorno VERIFIK_* (fuente de verdad: .env.verifik.example).
/// </summary>
public sealed class VerifikOptions
{
    public const string SectionName = "Verifik";

    public string BaseUrl { get; set; } = "https://api.verifik.co";
    public string ApiToken { get; set; } = string.Empty;
    public string AuthScheme { get; set; } = "Bearer";
    public int TimeoutSeconds { get; set; } = 30;
}
