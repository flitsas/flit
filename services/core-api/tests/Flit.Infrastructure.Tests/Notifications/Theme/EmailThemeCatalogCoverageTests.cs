using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications;
using Flit.Infrastructure.Notifications.Catalog;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Theme;

/// <summary>
/// Uso de ejemplo:
/// var (subject, html) = NotificationSampleRenderer.Render(descriptor.Id, NotificationChannel.FlitSmtp, theme: brandTheme);
/// HU #12428 AC8 — TODA plantilla del catálogo (canal FLIT) debe renderizar con un tema
/// <c>Brand</c> y contener el chrome de marca; con <c>Flit</c>/<c>null</c> no debe contener el
/// nombre de la marca de prueba (para no confundir el respaldo con un renderizado "a medias").
/// </summary>
public sealed class EmailThemeCatalogCoverageTests
{
    private static readonly EmailTheme BrandTheme = new(
        EmailThemeKind.Brand,
        "Movilidad Andina Cobertura",
        "https://dev.flitsas.online/api/v1/public/branding/logos/11111111-1111-4111-8111-111111111111",
        "#123ABC",
        "#456DEF",
        "#FFFFFF",
        7);

    public static IEnumerable<object[]> AllTemplateIds() =>
        NotificationTemplateCatalog.All.Select(d => new object[] { d.Id });

    [Theory]
    [MemberData(nameof(AllTemplateIds))]
    public void AC8_TodaPlantillaDelCatalogo_ConTemaBrand_ContieneNombrePlataformaLogoAbsolutoYColorPrimario(string templateId)
    {
        var (_, html) = NotificationSampleRenderer.Render(
            templateId, NotificationChannel.FlitSmtp, procedureType: RequiresProcedureTypeOverlay(templateId), theme: BrandTheme);

        html.Should().Contain(BrandTheme.PlatformName, $"'{templateId}' debe mostrar el nombre de la marca (AC2/AC8)");
        html.Should().Contain(BrandTheme.LogoUrl!, $"'{templateId}' debe referenciar el logo absoluto (AC3)");
        html.Should().ContainAny(new[] { BrandTheme.Primary, BrandTheme.Primary.ToLowerInvariant() });
    }

    [Theory]
    [MemberData(nameof(AllTemplateIds))]
    public void AC9_TodaPlantillaDelCatalogo_ConTemaFlitONulo_NoContieneNombreDeLaMarcaDePrueba(string templateId)
    {
        var (_, htmlNull) = NotificationSampleRenderer.Render(
            templateId, NotificationChannel.FlitSmtp, procedureType: RequiresProcedureTypeOverlay(templateId));
        var (_, htmlFlit) = NotificationSampleRenderer.Render(
            templateId, NotificationChannel.FlitSmtp, procedureType: RequiresProcedureTypeOverlay(templateId), theme: EmailTheme.Flit);

        htmlNull.Should().NotContain(BrandTheme.PlatformName);
        htmlFlit.Should().NotContain(BrandTheme.PlatformName);
        htmlNull.Should().Be(htmlFlit, "theme=null y theme=EmailTheme.Flit deben producir el mismo HTML (AC9)");
    }

    private static NotificationSampleProcedureType? RequiresProcedureTypeOverlay(string templateId) =>
        TramiteCambioEstadoEmailComposer.RequiresProcedureType(templateId)
            ? new NotificationSampleProcedureType("Matrícula inicial", false)
            : null;
}
