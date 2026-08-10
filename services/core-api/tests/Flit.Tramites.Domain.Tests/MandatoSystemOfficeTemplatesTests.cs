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
    public void ResolveTemplateCode_SabanetaConfigGenericoSinCustom_IgualSistema()
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("5631000", "generico", MandatoCustomTemplateKindCodes.None)
            .Should().Be("sabaneta");
    }

    [Fact]
    public void ResolveTemplateCode_OtroOtConConfig_UsaConfig()
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("9999999", "bello", null)
            .Should().Be("bello");
    }

    [Fact]
    public void ResolveTemplateCode_EnvigadoConfigGenericoSinCustom_IgualSistema()
    {
        MandatoSystemOfficeTemplates
            .ResolveTemplateCode("5266000", "generico", MandatoCustomTemplateKindCodes.None)
            .Should().Be("municipio");
    }
}
