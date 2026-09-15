namespace Flit.Admin.Domain.Companies.Branding;

/// <summary>
/// Forma compartida del borrador editable y del snapshot publicado (jsonb <c>schemaVersion=1</c> de
/// <c>admin.tenant_brandings</c>, HU #12412 ADR-0060 D1). El borrador puede estar incompleto
/// (cualquier campo <c>null</c>); el publicado, cuando existe, es el que resuelven <c>/public/branding</c>,
/// <c>/me/branding</c> y el tema de correo (#12418/#12428).
/// </summary>
public sealed record BrandingDraft(string? PlatformName, BrandColors? Colors, Guid? LogoId)
{
    public const int SchemaVersion = 1;

    public static readonly BrandingDraft Empty = new(null, null, null);

    /// <summary>Presencia estructural (no formato/contraste): usada para <c>completeness</c> en el
    /// contrato de lectura. No bloquea publicar en esta HU (ver <see cref="Flit.Admin.Application.Companies.Branding.IBrandAssetValidator"/>).</summary>
    public IReadOnlyList<string> MissingFields()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(PlatformName))
        {
            missing.Add("platformName");
        }

        if (Colors is null)
        {
            missing.Add("colors");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Colors.Primary)) missing.Add("colors.primary");
            if (string.IsNullOrWhiteSpace(Colors.Secondary)) missing.Add("colors.secondary");
            if (string.IsNullOrWhiteSpace(Colors.OnPrimary)) missing.Add("colors.onPrimary");
        }

        if (LogoId is null)
        {
            missing.Add("logo");
        }

        return missing;
    }
}
