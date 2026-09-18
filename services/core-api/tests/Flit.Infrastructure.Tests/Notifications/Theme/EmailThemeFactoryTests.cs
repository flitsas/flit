using Flit.Admin.Domain.Companies.Branding;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Theme;

/// <summary>
/// Uso de ejemplo:
/// var theme = EmailThemeFactory.FromPublished(draft, publishedVersion: 3, "https://dev.flitsas.online");
/// HU #12428 AC1/AC2/AC3 — construcción del tema a partir de un snapshot de marca (publicado o
/// borrador) + URL pública absoluta del logotipo.
/// </summary>
public sealed class EmailThemeFactoryTests
{
    private const string PublicBaseUrl = "https://dev.flitsas.online";
    private static readonly BrandColors Colors = new("#0B3D91", "#1FA2FF", "#FFFFFF");

    [Fact]
    public void FromPublished_ConLogo_ConstruyeUrlAbsolutaVersionada()
    {
        var logoId = Guid.NewGuid();
        var draft = new BrandingDraft("Movilidad Andina", Colors, logoId);

        var theme = EmailThemeFactory.FromPublished(draft, publishedVersion: 3, PublicBaseUrl);

        theme.Kind.Should().Be(EmailThemeKind.Brand);
        theme.LogoUrl.Should().Be($"{PublicBaseUrl}/api/v1/public/branding/logos/{logoId}");
        theme.Version.Should().Be(3);
        theme.PlatformName.Should().Be("Movilidad Andina");
        theme.Primary.Should().Be(Colors.Primary);
    }

    [Fact]
    public void FromPublished_SinLogo_LogoUrlEsNull()
    {
        var draft = new BrandingDraft("Movilidad Andina", Colors, null);

        var theme = EmailThemeFactory.FromPublished(draft, publishedVersion: 1, PublicBaseUrl);

        theme.LogoUrl.Should().BeNull();
    }

    [Fact]
    public void FromDraft_BorradorIncompletoSinNombreNiColores_CompletaConValoresFlit()
    {
        var draft = new BrandingDraft(null, null, null);

        var theme = EmailThemeFactory.FromDraft(draft, PublicBaseUrl);

        theme.PlatformName.Should().Be(EmailTheme.Flit.PlatformName);
        theme.Primary.Should().Be(EmailTheme.Flit.Primary);
        theme.Secondary.Should().Be(EmailTheme.Flit.Secondary);
        theme.OnPrimary.Should().Be(EmailTheme.Flit.OnPrimary);
        theme.LogoUrl.Should().BeNull();
    }

    [Fact]
    public void FromDraft_ConDatosParciales_UsaLosInformadosYCompletaElResto()
    {
        var draft = new BrandingDraft("Movilidad Andina", null, null);

        var theme = EmailThemeFactory.FromDraft(draft, PublicBaseUrl);

        theme.PlatformName.Should().Be("Movilidad Andina");
        theme.Primary.Should().Be(EmailTheme.Flit.Primary);
    }
}
