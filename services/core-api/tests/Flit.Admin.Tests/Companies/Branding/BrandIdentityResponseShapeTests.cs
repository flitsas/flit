using System.Text.Json;
using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo: <c>JsonSerializer.Serialize(BrandIdentityResponse.From(BrandIdentity.Flit))</c>.
/// HU #12418 AC3 — la respuesta pública/sesión NUNCA lleva id de tenant, NIT, correo, razón social,
/// lista de hijos ni flags <c>isDefault</c>/<c>isNetwork</c>.
/// </summary>
public sealed class BrandIdentityResponseShapeTests
{
    // Minimal APIs serializan con los defaults "Web" (camelCase) — igual que Results.Ok en
    // PublicBrandingEndpoints/MeBrandingEndpoints.
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] ForbiddenFieldNames =
    [
        "tenantId", "tenant_id", "id", "nit", "taxId", "tax_id", "email", "correo",
        "legalName", "legal_name", "razonSocial", "razon_social", "children", "hijos",
        "isDefault", "is_default", "isNetwork", "is_network", "headTenantId", "head_tenant_id",
    ];

    [Fact]
    public void AC3_IdentidadFlit_SerializadaSinCamposProhibidos()
    {
        var json = JsonSerializer.Serialize(BrandIdentityResponse.From(BrandIdentity.Flit), WebOptions);

        AssertNoForbiddenField(json);
    }

    [Fact]
    public void AC3_MarcaPublicada_SerializadaSinCamposProhibidos()
    {
        var identity = new BrandIdentity(
            "Movilidad Andina",
            Guid.NewGuid(),
            new BrandColors("#0B3D91", "#1FA2FF", "#FFFFFF"),
            3);

        var json = JsonSerializer.Serialize(BrandIdentityResponse.From(identity), WebOptions);

        AssertNoForbiddenField(json);
        json.Should().Contain("platformName").And.Contain("logoUrl").And.Contain("colors").And.Contain("version");
    }

    [Fact]
    public void AC3_LogoUrl_EsSiempreLaRutaPublicaPorVersion_NuncaUnaRutaDeStorage()
    {
        var logoId = Guid.NewGuid();
        var identity = new BrandIdentity("X", logoId, new BrandColors("#000000", "#000000", "#FFFFFF"), 1);

        var response = BrandIdentityResponse.From(identity);

        response.LogoUrl.Should().Be($"/api/v1/public/branding/logos/{logoId}");
    }

    [Fact]
    public void AC1_FormaDeLaRespuesta_TieneExactamenteLosCuatroCampos()
    {
        var response = BrandIdentityResponse.From(BrandIdentity.Flit);
        var element = JsonSerializer.SerializeToElement(response, WebOptions);

        element.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["platformName", "logoUrl", "colors", "version"]);
    }

    private static void AssertNoForbiddenField(string json)
    {
        using var document = JsonDocument.Parse(json);
        var propertyNames = CollectPropertyNames(document.RootElement).ToList();

        foreach (var forbidden in ForbiddenFieldNames)
        {
            propertyNames.Should().NotContain(
                name => string.Equals(name, forbidden, StringComparison.OrdinalIgnoreCase),
                $"la respuesta pública no debe exponer el campo '{forbidden}' (AC3)");
        }
    }

    private static IEnumerable<string> CollectPropertyNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var property in element.EnumerateObject())
        {
            yield return property.Name;
            foreach (var nested in CollectPropertyNames(property.Value))
            {
                yield return nested;
            }
        }
    }
}
