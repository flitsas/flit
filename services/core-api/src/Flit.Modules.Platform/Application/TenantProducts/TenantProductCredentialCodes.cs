namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>Lista blanca v1: solo <c>(diagnostico, impuesto_api_key)</c>.</summary>
internal static class TenantProductCredentialCodes
{
    public const string Diagnostico = "diagnostico";
    public const string ImpuestoApiKey = "impuesto_api_key";

    public static bool IsSupported(string productCode, string credentialCode) =>
        string.Equals(productCode, Diagnostico, StringComparison.Ordinal)
        && string.Equals(credentialCode, ImpuestoApiKey, StringComparison.Ordinal);

    public static (string Product, string Credential) Normalize(string? productCode, string? credentialCode) =>
        (productCode?.Trim().ToLowerInvariant() ?? string.Empty, credentialCode?.Trim().ToLowerInvariant() ?? string.Empty);
}
