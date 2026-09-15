using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Domain.Companies.Branding;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Companies.Branding;

/// <summary>
/// Uso de ejemplo:
/// var validator = new BrandAssetValidator(new BrandingOptions());
/// var errors = validator.ValidateDraft(draft);
/// HU #12413 AC1-AC6 — formato/peso/dimensiones/contraste con los <c>details</c> exactos del
/// contrato (<c>contratos-api.md</c> §3, <c>BrandingErrors.cs</c>).
/// </summary>
public sealed class BrandAssetValidatorTests
{
    private static BrandAssetValidator NewValidator() => new(new BrandingOptions());

    // ---- ValidateDraft — nombre (AC5) ----

    [Fact]
    public void ValidateDraft_NombreDemasiadoCorto_DevuelveNameLengthConCampo()
    {
        var draft = new BrandingDraft("A", null, null);
        var errors = NewValidator().ValidateDraft(draft);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.NameLength && e.Field == "platformName");
    }

    [Fact]
    public void ValidateDraft_NombreConEtiquetas_DevuelveNameMarkup()
    {
        var draft = new BrandingDraft("<b>Andina</b>", null, null);
        var errors = NewValidator().ValidateDraft(draft);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.NameMarkup && e.Field == "platformName");
    }

    [Fact]
    public void ValidateDraft_NombreValido_NoReportaErroresDeNombre()
    {
        var draft = new BrandingDraft("Movilidad Andina", null, null);
        var errors = NewValidator().ValidateDraft(draft);

        errors.Should().NotContain(e => e.Field == "platformName");
    }

    // ---- ValidateDraft — colores (AC3) ----

    [Theory]
    [InlineData("colors.primary")]
    [InlineData("colors.secondary")]
    [InlineData("colors.onPrimary")]
    public void ValidateDraft_ColorMalFormado_DevuelveColorFormatConElCampo(string field)
    {
        var colors = field switch
        {
            "colors.primary" => new BrandColors("no-es-hex", "#1FA2FF", "#FFFFFF"),
            "colors.secondary" => new BrandColors("#0B3D91", "no-es-hex", "#FFFFFF"),
            _ => new BrandColors("#0B3D91", "#1FA2FF", "no-es-hex"),
        };

        var draft = new BrandingDraft(null, colors, null);
        var errors = NewValidator().ValidateDraft(draft);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.ColorFormat && e.Field == field);
    }

    // ---- ValidateDraft — contraste (AC4) ----

    [Fact]
    public void ValidateDraft_ContrasteInsuficiente_DevuelveContrastTooLowConDetalles()
    {
        // onPrimary/primary con contraste insuficiente (#FFFFFF sobre #4F74C9 = 4.49 < 4.5);
        // onPrimary/secondary sí cumple (#FFFFFF sobre #162744 = 14.92) — un solo error esperado.
        var colors = new BrandColors("#4F74C9", "#162744", "#FFFFFF");
        var draft = new BrandingDraft(null, colors, null);

        var errors = NewValidator().ValidateDraft(draft);

        var contrastError = errors.Should().ContainSingle(e => e.Code == BrandingErrors.ContrastTooLow).Subject;
        contrastError.Details.Should().NotBeNull();
        contrastError.Details!["pair"].Should().Be("onPrimary/primary");
        contrastError.Details["ratio"].Should().Be(4.49);
        contrastError.Details["min"].Should().Be(4.5);
    }

    [Fact]
    public void ValidateDraft_UnParConSuficienteYOtroInsuficiente_ReportaSoloElParQueFalla()
    {
        var colors = new BrandColors("#162744", "#557EFF", "#FFFFFF"); // onPrimary/primary=14.92 (OK), onPrimary/secondary=3.61 (falla)
        var draft = new BrandingDraft(null, colors, null);

        var errors = NewValidator().ValidateDraft(draft);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.ContrastTooLow
            && "onPrimary/secondary".Equals(e.Details!["pair"]));
    }

    [Fact]
    public void ValidateDraft_ColorInvalido_NoIntentaCalcularContraste()
    {
        var colors = new BrandColors("no-es-hex", "#1FA2FF", "#FFFFFF");
        var draft = new BrandingDraft(null, colors, null);

        var errors = NewValidator().ValidateDraft(draft);

        errors.Should().NotContain(e => e.Code == BrandingErrors.ContrastTooLow);
    }

    [Fact]
    public void ValidateDraft_BorradorCompletoYValido_NoReportaErrores()
    {
        // FFFFFF/0B3D91 = 10.04, FFFFFF/162744 = 14.92 — ambos pares cumplen el mínimo 4.5:1.
        var colors = new BrandColors("#0B3D91", "#162744", "#FFFFFF");
        var draft = new BrandingDraft("Movilidad Andina", colors, Guid.NewGuid());

        NewValidator().ValidateDraft(draft).Should().BeEmpty();
    }

    // ---- ValidateLogo — formato/peso/dimensiones (AC1/AC2) ----

    [Fact]
    public void ValidateLogo_FormatoNoPermitido_DevuelveLogoFormat()
    {
        var errors = NewValidator().ValidateLogo("image/svg+xml", 1000, 200, 200);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoFormat);
    }

    [Fact]
    public void ValidateLogo_PesoMayorAlMaximo_DevuelveLogoTooLargeConMaxBytes()
    {
        var options = new BrandingOptions();
        var validator = new BrandAssetValidator(options);

        var errors = validator.ValidateLogo("image/png", options.Logo.MaxBytes + 1, 200, 200);

        var error = errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoTooLarge).Subject;
        error.Details!["maxBytes"].Should().Be(options.Logo.MaxBytes);
    }

    [Fact]
    public void ValidateLogo_DimensionesFueraDeRango_DevuelveLogoDimensionsConDetalles()
    {
        var errors = NewValidator().ValidateLogo("image/png", 1000, 100, 30);

        var error = errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoDimensions).Subject;
        error.Details.Should().NotBeNull();
    }

    [Fact]
    public void ValidateLogo_FormatoPesoYDimensionesValidos_NoReportaErrores()
    {
        var errors = NewValidator().ValidateLogo("image/png", 1000, 300, 100);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateLogo_LimitesParametrizables_UsanLaConfiguracionInyectada()
    {
        var options = new BrandingOptions { MinContrastRatio = 4.5 };
        options.Logo.MaxBytes = 1000;
        options.Logo.MinWidth = 50;
        options.Logo.MinHeight = 50;
        var validator = new BrandAssetValidator(options);

        // 40x40 está bajo el mínimo parametrizado (50x50), aunque cumpla el default de 120x40.
        var errors = validator.ValidateLogo("image/png", 500, 40, 40);

        errors.Should().ContainSingle(e => e.Code == BrandingErrors.LogoDimensions);
    }
}
