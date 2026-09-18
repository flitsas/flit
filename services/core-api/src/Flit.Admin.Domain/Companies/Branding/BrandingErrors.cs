namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Vocabulario estable de códigos de error de la identidad de marca (contrato
/// <c>contracts/openapi/core-api.v1.yaml</c>, HU #12412/#12413). El configurador de marca (#12414)
/// muestra texto por código, así que estas cadenas son parte del contrato público — no renombrar.
/// </summary>
public static class BrandingErrors
{
    public const string NotFound = "BRANDING_NOT_FOUND";
    public const string TenantNotMarcaBlanca = "BRANDING_TENANT_NOT_MARCA_BLANCA";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string Incomplete = "BRANDING_INCOMPLETE";

    /// <summary>HU #12428/#12431 — <c>templateId</c> fuera de <c>Branding:SampleTemplates</c> en
    /// <c>GET /company/branding/email-sample</c>.</summary>
    public const string SampleTemplateNotAllowed = "BRANDING_SAMPLE_TEMPLATE_NOT_ALLOWED";

    // Formato/contraste — vocabulario reservado para #12413 (IBrandAssetValidator permisivo hoy).
    public const string NameLength = "BRANDING_NAME_LENGTH";
    public const string NameMarkup = "BRANDING_NAME_MARKUP";
    public const string ColorFormat = "BRANDING_COLOR_FORMAT";
    public const string ContrastTooLow = "BRANDING_CONTRAST_TOO_LOW";
    public const string LogoNotFound = "BRANDING_LOGO_NOT_FOUND";
    public const string LogoFormat = "BRANDING_LOGO_FORMAT";
    public const string LogoTooLarge = "BRANDING_LOGO_TOO_LARGE";
    public const string LogoDimensions = "BRANDING_LOGO_DIMENSIONS";
}
