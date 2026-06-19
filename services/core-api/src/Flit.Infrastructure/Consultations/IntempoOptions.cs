namespace Flit.Infrastructure.Consultations;

/// <summary>
/// Configuración del proveedor INTEMPO (Moviliza). Sin auth: acceso por IP/contrato.
/// Variables de entorno: INTEMPO_BASE_URL, INTEMPO_TIMEOUT_SECONDS.
/// </summary>
public sealed class IntempoOptions
{
    public const string SectionName = "Intempo";

    public string BaseUrl { get; set; } = "https://www.moviliza.com.co";
    public int TimeoutSeconds { get; set; } = 15;
}
