using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Ocr;

public sealed class DocumentOcrPromptsTests
{
    [Theory]
    [InlineData("factura")]
    [InlineData("aduana")]
    [InlineData("impronta")]
    [InlineData("soat")]
    public void Tipos_soportados_tienen_prompt(string tipo)
    {
        DocumentOcrPrompts.IsSupported(tipo).Should().BeTrue();
        DocumentOcrPrompts.PromptFor(tipo).Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("cotizacion")]
    [InlineData("otro")]
    [InlineData("")]
    public void Tipos_no_soportados_no_tienen_prompt(string tipo)
    {
        DocumentOcrPrompts.IsSupported(tipo).Should().BeFalse();
        DocumentOcrPrompts.PromptFor(tipo).Should().BeNull();
    }

    [Fact]
    public void IsSupported_null_es_false()
    {
        DocumentOcrPrompts.IsSupported(null).Should().BeFalse();
    }

    [Fact]
    public void Todos_los_prompts_incluyen_el_bloque_multipagina()
    {
        foreach (var tipo in DocumentOcrPrompts.SupportedTipos)
        {
            var prompt = DocumentOcrPrompts.PromptFor(tipo);
            prompt.Should().Contain("DOCUMENTO MULTIPAGINA");
            prompt.Should().Contain("paginas_documento");
            prompt.Should().Contain("JSON valido sin markdown");
        }
    }

    [Fact]
    public void Prompt_factura_pide_campo_de_validez_de_factura()
    {
        DocumentOcrPrompts.PromptFor("factura").Should().Contain("es_factura_valida");
    }
}
