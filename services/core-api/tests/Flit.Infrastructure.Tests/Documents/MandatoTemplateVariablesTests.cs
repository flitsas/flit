using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #13170 (Feature #13118, Épica #13090) — registro único de variables de plantilla de mandato y su
/// validador. Uso: <c>MandatoTemplateValidator.Validate("Placa {{placa}}")</c> es válido;
/// <c>Validate("{{cedula_inventada}}")</c> falla con <c>plantilla_variable_invalida</c>.
/// </summary>
public sealed class MandatoTemplateVariablesTests
{
    [Fact]
    public void AC1_Registro_IncluyeLasVariablesBaseYLasDeFirma()
    {
        MandatoTemplateValidator.AllowedVariables.Should().Contain(
        [
            "placa", "tramite", "organismo", "ciudad", "fecha", "mandante_nombre", "mandante_documento",
            "mandatario_nombre", "mandatario_documento", "fecha_firma", "fecha_hora_firma_mandante",
            "fecha_firma_mandatario",
        ]);
    }

    [Fact]
    public void AC2_PlantillaQueSoloUsaVariablesDelRegistro_EsValida()
    {
        var result = MandatoTemplateValidator.Validate(
            "Yo {{mandante_nombre}} con {{mandante_documento}} otorgo mandato sobre {{placa}}.\nFirmado {{fecha_firma}}.");

        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
        result.UnknownVariables.Should().BeEmpty();
    }

    [Fact]
    public void AC3_VariableDesconocida_SeRechazaConLaListaYSuPosicion()
    {
        var result = MandatoTemplateValidator.Validate("Linea uno {{placa}}\nCedula {{cedula_inventada}} y {{otra}}");

        result.Error.Should().Be("plantilla_variable_invalida");
        result.UnknownVariables.Select(v => (v.Name, v.Line, v.Column)).Should().Equal(
            ("cedula_inventada", 2, 8), ("otra", 2, 31));
    }

    [Theory]
    [InlineData("Placa {{placa")]
    [InlineData("{{placa {{fecha}}")]
    public void AC4_LlavesSinCerrar_SeRechazanPorSintaxis(string body) =>
        MandatoTemplateValidator.Validate(body).Error.Should().Be("plantilla_sintaxis_invalida");

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    [InlineData(null)]
    public void AC5_PlantillaVacia_SeRechaza(string? body) =>
        MandatoTemplateValidator.Validate(body).Error.Should().Be("plantilla_vacia");

    [Fact]
    public void AC6_NombresDeFlit1_SoloSeAceptanSiElRegistroDefineLaEquivalencia()
    {
        MandatoTemplateValidator.Validate("{{nombre_mandante}} {{nombre_union_temporal}} {{cedula_mandante}}")
            .IsValid.Should().BeTrue();

        var result = MandatoTemplateValidator.Validate("{{hash_mandante}} {{razon_social_mandante}}");
        result.Error.Should().Be("plantilla_variable_invalida");
        result.UnknownVariables.Select(v => v.Name).Should().Equal("hash_mandante", "razon_social_mandante");
    }

    [Fact]
    public void LosEspaciosDentroDeLasLlaves_SeRechazan_PorqueElGeneradorNoLosSustituiria()
    {
        MandatoTemplateValidator.Validate("{{ placa }}").Error.Should().Be("plantilla_variable_invalida");
    }

    [Fact]
    public void AC7_FechaFirma_EsVariable_SeAceptaYSeLlenaConElEventoDelTramite()
    {
        MandatoTemplateValidator.Validate("Firmado el {{fecha_firma}}").IsValid.Should().BeTrue();

        var data = MandatoPreviewSample.Build("generico") with
        {
            CustomTemplateKind = "editor",
            CustomTemplateBody = "{{fecha_firma}}|{{fecha_hora_firma_mandante}}|{{fecha_firma_mandatario}}",
        };
        var fecha = data.Tramite.FechaTramite!.Value;

        var text = string.Concat(MandatoPdfGenerator.ApplyPlaceholdersSegmented(data.CustomTemplateBody!, data)
            .SelectMany(l => l).Select(s => s.Texto));

        text.Should().Contain($"{fecha.Day} de ");
        text.Should().Contain(fecha.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        text.Should().NotContain("{{");
    }

    [Fact]
    public void AC8_TodaVariableDelRegistro_EsSustituidaPorElGenerador()
    {
        var data = MandatoPreviewSample.Build("generico");
        var body = string.Join("\n", MandatoTemplateVariables.AcceptedNames.Select(n => "{{" + n + "}}"));

        MandatoTemplateValidator.Validate(body).IsValid.Should().BeTrue();
        var lineas = MandatoPdfGenerator.ApplyPlaceholdersSegmented(body, data);

        lineas.Should().HaveCount(MandatoTemplateVariables.AcceptedNames.Count());
        lineas.SelectMany(l => l).Select(s => s.Texto).Should().OnlyContain(t => !t.Contains("{{"));
    }

    [Fact]
    public void Alias_DeFlit1_SeSustituyeConElMismoValorQueElNombreCanonico()
    {
        var data = MandatoPreviewSample.Build("generico");

        string Render(string body) => string.Concat(
            MandatoPdfGenerator.ApplyPlaceholdersSegmented(body, data).SelectMany(l => l).Select(s => s.Texto));

        Render("{{nombre_mandante}}").Should().Be(Render("{{mandante_nombre}}"));
        Render("{{cedula_mandatario}}").Should().Be(Render("{{mandatario_documento}}"));
    }

    [Fact]
    public void LasPlantillasDeSistemaYElEditor_GeneranPdfConLaMismaPlantillaValida()
    {
        var data = MandatoPreviewSample.Build("generico") with
        {
            CustomTemplateKind = "editor",
            CustomTemplateBody = "Contrato sobre {{placa}} de {{mandante_nombre}}",
        };

        var doc = new MandatoPdfGenerator().GenerateMandato(data);

        System.Text.Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }
}
