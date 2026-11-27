namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>Lista blanca: <c>diagnostico</c> con <c>impuesto_api_key</c>, <c>impuesto_webhook_secret</c> e <c>impuesto_webhook_secret_anterior</c> (HU #14711).</summary>
internal static class TenantProductCredentialCodes
{
    public const string Diagnostico = "diagnostico";
    public const string ImpuestoApiKey = "impuesto_api_key";
    public const string ImpuestoWebhookSecret = "impuesto_webhook_secret";
    public const string ImpuestoWebhookSecretAnterior = "impuesto_webhook_secret_anterior";

    /// <summary>Vigencia del secreto anterior tras una rotación: se mide desde su <c>updated_at</c>.</summary>
    public static readonly TimeSpan VentanaSecretoAnterior = TimeSpan.FromHours(24);

    /// <summary>Solo la api_key se gestiona a mano desde el admin (PUT/DELETE); los secretos de webhook solo los escribe el alta.</summary>
    public static bool IsSupported(string productCode, string credentialCode) =>
        string.Equals(productCode, Diagnostico, StringComparison.Ordinal)
        && string.Equals(credentialCode, ImpuestoApiKey, StringComparison.Ordinal);

    public static bool IsWebhookSecret(string credentialCode) =>
        string.Equals(credentialCode, ImpuestoWebhookSecret, StringComparison.Ordinal)
        || string.Equals(credentialCode, ImpuestoWebhookSecretAnterior, StringComparison.Ordinal);

    public static (string Product, string Credential) Normalize(string? productCode, string? credentialCode) =>
        (productCode?.Trim().ToLowerInvariant() ?? string.Empty, credentialCode?.Trim().ToLowerInvariant() ?? string.Empty);
}
