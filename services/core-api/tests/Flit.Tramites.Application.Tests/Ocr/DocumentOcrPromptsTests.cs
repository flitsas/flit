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
    [InlineData("rtm")] // HU #10977
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

    // ── HU #10976 (Feature #10972) — prompt de SOAT v2 ───────────────────────

    [Fact]
    public void Prompt_soat_v2_pide_la_fecha_de_expedicion()
    {
        var prompt = DocumentOcrPrompts.PromptFor("soat")!;

        prompt.Should().Contain("fecha_expedicion");
        // También debe declararse en el JSON de salida, o el modelo puede omitirla.
        prompt.Should().Contain("\"fecha_expedicion\":\"\"");
    }

    [Fact]
    public void Prompt_soat_v2_distingue_expedicion_de_inicio_de_vigencia()
    {
        // Sin esta instrucción el modelo tiende a copiar la misma fecha en ambos campos y el
        // certificado mostraría dos celdas idénticas.
        DocumentOcrPrompts.PromptFor("soat")!.Should().Contain("NO copiar aqui la fecha de inicio de vigencia");
    }

    [Theory]
    // v2 es ADITIVO: ningún campo de v1 puede desaparecer (contrato con la persistencia de HU #10975).
    [InlineData("numero_poliza")]
    [InlineData("aseguradora")]
    [InlineData("fecha_inicio")]
    [InlineData("fecha_vencimiento")]
    [InlineData("estado_poliza")]
    public void Prompt_soat_v2_conserva_los_campos_de_v1(string campo)
    {
        DocumentOcrPrompts.PromptFor("soat")!.Should().Contain(campo);
    }

    // ── HU #10977 (Feature #10972) — prompt de RTM ───────────────────────────

    [Theory]
    [InlineData("numero_certificado")]
    [InlineData("cda_expide")]
    [InlineData("fecha_expedicion")]
    [InlineData("fecha_vigencia")]
    [InlineData("fecha_vencimiento")]
    [InlineData("estado")]
    public void Prompt_rtm_pide_los_campos_del_certificado(string campo)
    {
        DocumentOcrPrompts.PromptFor("rtm")!.Should().Contain(campo);
    }

    [Fact]
    public void Prompt_rtm_rechaza_explicitamente_el_soat()
    {
        // Ambos conviven en el mismo expediente y se parecen: el prompt debe descartar el cruce
        // para no poblar rtm_* con datos de la póliza.
        DocumentOcrPrompts.PromptFor("rtm")!.Should().Contain("NO es valido si es: una poliza SOAT");
    }

    [Fact]
    public void Prompt_rtm_prohibe_deducir_fechas_ausentes()
    {
        DocumentOcrPrompts.PromptFor("rtm")!.Should().Contain("NO inventes ni deduzcas fechas");
    }
}
