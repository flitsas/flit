using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>
/// Uso de ejemplo:
/// var (_, html) = RevocationRequestEmailComposer.ComposeFlit(milestone, model, assetsBaseUrl, brandTheme);
/// HU #12428 (Feature #12405) sobre las 3 plantillas de revocatoria (#12565): con tema
/// <c>Brand</c> el composer y la muestra emiten el chrome de marca (nombre, logo absoluto, color
/// primario) y NO el chrome FLIT; con <c>Flit</c>/<c>null</c> emiten el chrome FLIT byte a byte y
/// nunca la marca (AC1/AC2/AC3/AC8/AC9).
/// </summary>
public sealed class RevocationRequestEmailComposerThemeTests
{
    private const string AssetsBaseUrl = "https://cdn.flit.test/email-assets";

    private static readonly EmailTheme BrandTheme = new(
        EmailThemeKind.Brand,
        "Movilidad Andina Revocatoria",
        "https://dev.flitsas.online/api/v1/public/branding/logos/22222222-2222-4222-8222-222222222222",
        "#0B3D91",
        "#1FA2FF",
        "#FFFFFF",
        7);

    private static RevocationRequestEmailModel Model(string? motivo = null) => new(
        DestinatarioNombre: "Ana Radicadora",
        Placa: "ABC123",
        Radicado: "FT1-0000099",
        Motivo: motivo);

    public static IEnumerable<object[]> Milestones() =>
    [
        [RevocationRequestEmailMilestone.Solicitada],
        [RevocationRequestEmailMilestone.Aprobada],
        [RevocationRequestEmailMilestone.Rechazada],
    ];

    [Theory]
    [MemberData(nameof(Milestones))]
    public void ComposeFlit_ConTemaBrand_EmiteChromeDeMarcaYConservaDatosDelHito(string milestone)
    {
        var (subject, html) = RevocationRequestEmailComposer.ComposeFlit(
            milestone, Model("El soporte no es legible."), AssetsBaseUrl, BrandTheme);

        html.Should().Contain(BrandTheme.PlatformName, "AC2 — nombre de la marca en encabezado/pie");
        html.Should().Contain(BrandTheme.LogoUrl!, "AC3 — logotipo por URL absoluta");
        html.Should().Contain(BrandTheme.Primary, "AC2 — color primario en título/enlaces");
        html.Should().NotContain("tramite-cambio-estado-header.png", "el chrome FLIT no se mezcla con el de marca");
        html.Should().NotContain("alt=\"FLIT Version 2.0\"");
        html.Should().NotContain("En FLIT,", "el cierre de marca no menciona a FLIT como remitente");

        html.Should().Contain("Ana Radicadora");
        html.Should().Contain("FT1-0000099");
        html.Should().Contain("ABC123");
        subject.Should().Contain("FT1-0000099");

        if (milestone == RevocationRequestEmailMilestone.Rechazada)
        {
            html.Should().Contain("Motivo:");
            html.Should().Contain("El soporte no es legible.");
        }
        else
        {
            html.Should().NotContain("Motivo:");
        }
    }

    [Theory]
    [MemberData(nameof(Milestones))]
    public void ComposeFlit_ConTemaFlitONulo_EmiteChromeFlitIdenticoYSinMarca(string milestone)
    {
        var (_, htmlNull) = RevocationRequestEmailComposer.ComposeFlit(milestone, Model("x"), AssetsBaseUrl);
        var (_, htmlFlit) = RevocationRequestEmailComposer.ComposeFlit(milestone, Model("x"), AssetsBaseUrl, EmailTheme.Flit);

        htmlFlit.Should().Be(htmlNull, "AC9 — theme=null y theme=EmailTheme.Flit deben ser byte a byte iguales");
        htmlNull.Should().Contain("tramite-cambio-estado-header.png", "chrome FLIT de respaldo");
        htmlNull.Should().Contain("alt=\"FLIT Version 2.0\"");
        htmlNull.Should().NotContain(BrandTheme.PlatformName);
        htmlNull.Should().NotContain(BrandTheme.LogoUrl!);
    }

    [Theory]
    [MemberData(nameof(Milestones))]
    public void PreviewSample_BuildFlit_ConTemaBrand_EmiteChromeDeMarca(string milestone)
    {
        var (_, html) = RevocationRequestEmailPreviewSample.BuildFlit(milestone, AssetsBaseUrl, BrandTheme);

        html.Should().Contain(BrandTheme.PlatformName);
        html.Should().Contain(BrandTheme.LogoUrl!);
        html.Should().Contain(BrandTheme.Primary);
        html.Should().Contain("FT1-0000012", "la muestra conserva su radicado de ejemplo");
    }

    [Theory]
    [MemberData(nameof(Milestones))]
    public void PreviewSample_BuildFlit_ConTemaFlitONulo_EsIdenticoAlAnterior(string milestone)
    {
        var (_, htmlNull) = RevocationRequestEmailPreviewSample.BuildFlit(milestone, AssetsBaseUrl);
        var (_, htmlFlit) = RevocationRequestEmailPreviewSample.BuildFlit(milestone, AssetsBaseUrl, EmailTheme.Flit);

        htmlFlit.Should().Be(htmlNull);
        htmlNull.Should().NotContain(BrandTheme.PlatformName);
    }

    [Theory]
    [MemberData(nameof(Milestones))]
    public void ComposeRenting_IgnoraElTema_NoHaySobrecargaConTheme(string milestone)
    {
        // AC8 — el canal TenantApi (Renting) no se tematiza: el composer no expone un parámetro
        // de tema en ComposeRenting, así que el HTML es el de siempre.
        var (_, html) = RevocationRequestEmailComposer.ComposeRenting(milestone, Model(), AssetsBaseUrl);

        html.Should().Contain("Renting Colombia");
        html.Should().NotContain(BrandTheme.PlatformName);
    }
}
