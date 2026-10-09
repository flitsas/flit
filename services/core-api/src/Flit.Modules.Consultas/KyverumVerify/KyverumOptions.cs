namespace Flit.Infrastructure.Kyverum;

/// <summary>
/// Configuración del proveedor Kyverum Verify (HU #10233). Valores desde <c>Kyverum:*</c> (config) con
/// fallback a env <c>KYVERUM_*</c> — mismo patrón que <c>VerifikOptions</c>. La API key y el secreto del
/// webhook NUNCA se loguean ni se exponen en DTOs.
/// </summary>
public sealed class KyverumOptions
{
    public const string SectionName = "Kyverum";

    public string BaseUrl { get; set; } = "https://verify.kyverum.com";
    public string ApiKey { get; set; } = string.Empty;
    public string AuthScheme { get; set; } = "Bearer";
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// URL pública BASE del webhook FLIT (vía Gateway). El cliente le añade el id de correlación de cada
    /// validación. Kyverum exige una URL pública (rechaza host local). El secreto de firma NO va aquí:
    /// Kyverum lo devuelve por-validación en el create y se persiste cifrado.
    /// </summary>
    public string WebhookCallbackUrl { get; set; } = string.Empty;
}
