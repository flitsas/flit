namespace Flit.Infrastructure.Rues;

/// <summary>
/// Configuración del proveedor de autogeneración del Certificado RUES (RF36). Valores desde
/// <c>Rues:*</c> (config) con fallback a env <c>RUES_*</c> — mismo patrón que
/// <see cref="Improntas.ImprontaRuntOptions"/>. La integración es OPT-IN: con <see cref="Enabled"/>
/// en <c>false</c> (por defecto en los 3 ambientes) el cliente NO se registra y el trámite usa la
/// carga manual del RUES. La API key NUNCA se loguea ni se expone en DTOs.
/// </summary>
public sealed class RuesOptions
{
    public const string SectionName = "Rues";

    /// <summary>Habilita la autogeneración. Por defecto <c>false</c> (respaldo manual en todos los ambientes).</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>API key del proveedor. Solo el token, sin el prefijo del scheme.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string AuthScheme { get; set; } = "Bearer";

    public int TimeoutSeconds { get; set; } = 30;
}
