namespace Flit.Infrastructure.Consultations;

/// <summary>
/// FEATURE 05 — configuración del proveedor KYVERUM de comparendos para PERSONA JURÍDICA.
///
/// Deliberadamente separada de <c>ImprontaRuntOptions</c> (Kyverum RUNT), pese a compartir marca:
/// aquella está atada a runt.kyverum.com con credencial de alcance runt:read / impronta:generar.
/// Reutilizar esa credencial haría que un 401 por alcance insuficiente se presentara como una
/// caída del RUNT, contaminando un camino que hoy funciona. Env vars propias: KYVERUM_FINES_*.
///
/// ⚠️ <see cref="BaseUrl"/> e <see cref="InfractionPath"/> son PROVISIONALES: el proveedor aún no
/// entregó especificación ni credenciales. Por eso el modo por defecto es mock
/// (<see cref="ConsultationProviderModeOptions.KyverumFinesMode"/>). Confirmar ambos antes de
/// activar el modo real.
/// </summary>
public sealed class KyverumFinesOptions
{
    public const string SectionName = "KyverumFines";

    /// <summary>PROVISIONAL — sin confirmar por el proveedor.</summary>
    public string BaseUrl { get; set; } = "https://runt.kyverum.com";

    /// <summary>PROVISIONAL — sin confirmar por el proveedor.</summary>
    public string InfractionPath { get; set; } = "/v1/comparendos:consultar";

    public string ApiKey { get; set; } = string.Empty;

    public string AuthScheme { get; set; } = "Bearer";

    public int TimeoutSeconds { get; set; } = 30;
}
