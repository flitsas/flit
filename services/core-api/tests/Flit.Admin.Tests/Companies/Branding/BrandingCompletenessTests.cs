using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var missing = BrandingCompleteness.MissingFields(draft); // ["colors.secondary", "logo"]
/// HU #12413 AC6 — vocabulario estable de campos faltantes: platformName, colors.primary,
/// colors.secondary, colors.onPrimary, logo.
/// </summary>
public sealed class BrandingCompletenessTests
{
    private static readonly BrandColors CompleteColors = new("#0B3D91", "#1FA2FF", "#FFFFFF");

    [Fact]
    public void BorradorVacio_FaltanTodosLosCampos()
    {
        var missing = BrandingCompleteness.MissingFields(BrandingDraft.Empty);

        missing.Should().BeEquivalentTo(
            ["platformName", "colors.primary", "colors.secondary", "colors.onPrimary", "logo"]);
        BrandingCompleteness.IsComplete(BrandingDraft.Empty).Should().BeFalse();
    }

    [Fact]
    public void BorradorCompleto_NoFaltaNada()
    {
        var draft = new BrandingDraft("Movilidad Andina", CompleteColors, Guid.NewGuid());

        BrandingCompleteness.MissingFields(draft).Should().BeEmpty();
        BrandingCompleteness.IsComplete(draft).Should().BeTrue();
    }

    [Fact]
    public void FaltaSoloElLogo_ReportaLogo()
    {
        var draft = new BrandingDraft("Movilidad Andina", CompleteColors, null);

        BrandingCompleteness.MissingFields(draft).Should().BeEquivalentTo(["logo"]);
    }

    [Fact]
    public void FaltaUnColor_ReportaElColorEspecifico()
    {
        var draft = new BrandingDraft("Movilidad Andina", new BrandColors("#0B3D91", "", "#FFFFFF"), Guid.NewGuid());

        BrandingCompleteness.MissingFields(draft).Should().BeEquivalentTo(["colors.secondary"]);
    }

    [Fact]
    public void FaltaElNombre_ReportaPlatformName()
    {
        var draft = new BrandingDraft(null, CompleteColors, Guid.NewGuid());

        BrandingCompleteness.MissingFields(draft).Should().BeEquivalentTo(["platformName"]);
    }
}
