using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

public sealed class MandatoSystemOfficeTemplatesTests
{
    [Theory]
    [InlineData("5631000", MandatoTemplateResolver.Sabaneta)]
    [InlineData("5088000", MandatoTemplateResolver.Bello)]
    [InlineData("5266000", MandatoTemplateResolver.Municipio)]
    [InlineData("25286000", MandatoTemplateResolver.Municipio)]
    [InlineData("5001000", MandatoTemplateResolver.Municipio)]
    [InlineData("9999999", MandatoTemplateResolver.Generico)]
    [InlineData(null, MandatoTemplateResolver.Generico)]
    public void ResolveTemplateCode_SinCustom_BuiltinOGenerico(string? office, string expected)
    {
        MandatoSystemOfficeTemplates.ResolveTemplateCode(office ?? "", null, null)
            .Should().Be(expected);
    }

    [Fact]
    public void ResolveTemplateCode_SabanetaConCustom_ConservaCodigoConfigurado()
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("5631000", "sabaneta", MandatoCustomTemplateKindCodes.Pdf)
            .Should().Be("sabaneta");
    }

    [Fact]
    public void ResolveTemplateCode_SabanetaConfigGenerico_GanaLaEleccion()
    {
        // HU #11703 — la elección explícita del OT pasó a mandar sobre el builtin: antes esto
        // devolvía "sabaneta" y hacía inerte lo que se configurara para los cinco organismos.
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("5631000", "generico", MandatoCustomTemplateKindCodes.None)
            .Should().Be("generico");
    }

    [Fact]
    public void ResolveTemplateCode_OtroOtConConfig_UsaConfig()
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("9999999", "bello", null)
            .Should().Be("bello");
    }

    [Fact]
    public void ResolveTemplateCode_EnvigadoConfigGenerico_GanaLaEleccion()
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("5266000", "generico", MandatoCustomTemplateKindCodes.None)
            .Should().Be("generico");
    }

    [Theory]
    [InlineData("5631000", MandatoTemplateResolver.Sabaneta)]
    [InlineData("25286000", MandatoTemplateResolver.Municipio)]
    [InlineData("9999999", MandatoTemplateResolver.Generico)]
    public void ResolveTemplateCode_Auto_DelegaEnElBuiltinDelOrganismo(string office, string expected)
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode(office, MandatoTemplateResolver.Auto, MandatoCustomTemplateKindCodes.None)
            .Should().Be(expected);
    }

    [Fact]
    public void ResolveTemplateCode_FunzaConfigMunicipio_MantieneLoQueEmiteHoy()
    {
        // AC5 — las filas de los cinco organismos ya traen el código de su builtin: invertir la
        // precedencia no cambia lo que emiten.
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("25286000", "municipio", MandatoCustomTemplateKindCodes.None)
            .Should().Be("municipio");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("auto", true)]
    [InlineData("AUTO", true)]
    [InlineData("generico", false)]
    public void IsAuto_ReconoceLaAusenciaDeEleccion(string? code, bool expected)
    {
        MandatoTemplateResolver.IsAuto(code).Should().Be(expected);
    }

    [Fact]
    public void Resolve_Auto_NoEsUnaRedaccion_CaeAGenerico()
    {
        // "auto" nunca debería llegar al generador; si llega, no puede reventar.
        MandatoTemplateResolver.Resolve(MandatoTemplateResolver.Auto)
            .Should().Be(MandatoVariante.Generico);
    }

    [Theory]
    [InlineData("5631000", MandatoTemplateResolver.Sabaneta, MandatoFamiliaCodes.OrganismoTransito, "Medellín")]
    [InlineData("5088000", MandatoTemplateResolver.Bello, MandatoFamiliaCodes.OrganismoTransito, "Medellín")]
    [InlineData("5266000", MandatoTemplateResolver.Municipio, MandatoFamiliaCodes.Individuo, "Envigado")]
    [InlineData("25286000", MandatoTemplateResolver.Municipio, MandatoFamiliaCodes.Individuo, "Funza")]
    [InlineData("5001000", MandatoTemplateResolver.Municipio, MandatoFamiliaCodes.Individuo, "Medellín")]
    public void Birth_OtConocido_UsaSuPlantilla(string office, string template, string family, string city)
    {
        var birth = MandatoOtBirthDefaults.ForOffice(office);
        birth.TemplateCode.Should().Be(template);
        birth.MandataryFamily.Should().Be(family);
        birth.AssignmentMode.Should().Be(MandatoAssignmentModeCodes.Open);
        birth.ChamberCity.Should().Be(city);
    }

    [Fact]
    public void Birth_OtDesconocido_UsaGenerico()
    {
        var birth = MandatoOtBirthDefaults.ForOffice("11001000");
        birth.TemplateCode.Should().Be(MandatoTemplateResolver.Generico);
        birth.AssignmentMode.Should().Be(MandatoAssignmentModeCodes.Open);
        birth.MandataryFamily.Should().Be(MandatoFamiliaCodes.Individuo);
        birth.InstitutionalMandataryName.Should().BeNull();
    }
}
