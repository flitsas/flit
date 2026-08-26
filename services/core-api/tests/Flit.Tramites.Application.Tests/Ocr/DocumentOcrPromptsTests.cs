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

    // ── v3 — declaraciones de importación que amparan un lote ────────────────
    // Medido sobre 22 expedientes: aduana acertaba el VIN en 13 de 31 documentos porque una sola
    // declaración ampara 30-50 vehículos y el prompt pedía el esquema de uno.

    [Fact]
    public void Prompt_aduana_manda_vaciar_el_vin_cuando_la_declaracion_ampara_varios_vehiculos()
    {
        var prompt = DocumentOcrPrompts.PromptFor("aduana")!;

        prompt.Should().Contain("ampara_multiples_vehiculos");
        // Declarado también en el JSON de salida, o el modelo lo omite.
        prompt.Should().Contain("\"ampara_multiples_vehiculos\":false");
        // La regla que evita el peor fallo: un VIN plausible pero de otro vehículo del lote.
        prompt.Should().Contain("VACIOS");
        prompt.Should().Contain("NO uses el primero");
    }

    [Fact]
    public void Prompt_aduana_prohibe_concatenar_vins_y_numerar_campos()
    {
        // Las dos improvisaciones que rompían el parseo: 50 VIN en un campo, o vehiculo_vin_1..N
        // hasta agotar max_tokens y truncar el JSON.
        var prompt = DocumentOcrPrompts.PromptFor("aduana")!;

        prompt.Should().Contain("NO concatenes todos los VIN");
        prompt.Should().Contain("vehiculo_vin_1");
    }

    [Theory]
    [InlineData("factura")]
    [InlineData("aduana")]
    public void Prompts_de_factura_y_aduana_advierten_de_la_confusion_de_caracteres(string tipo)
    {
        // `impronta` ya la llevaba y acertó 35 de 35 VIN; factura falló uno justo por ahí
        // (leyó LGAX30139T9634772 en vez de LGAX3D139T9834772).
        var prompt = DocumentOcrPrompts.PromptFor(tipo)!;

        prompt.Should().Contain("0 vs O vs D");
        prompt.Should().Contain("EXACTAMENTE 17 caracteres");
    }

    [Fact]
    public void El_formato_FTH_002_queda_fuera_de_aduana_en_los_dos_extremos_del_pipeline()
    {
        // El clasificador lo proponía como aduana y el extractor lo rechazaba: la pieza llegaba a la
        // pantalla de revisión propuesta y marcada inválida a la vez.
        DocumentOcrPrompts.PromptFor("aduana")!.Should().Contain("FTH-002");
        DocumentOcrPrompts.ClassificationPrompt(["factura", "aduana", "impronta"])
            .Should().Contain("FTH-002");
    }

    [Fact]
    public void El_clasificador_sabe_que_una_declaracion_de_lote_ocupa_varias_paginas()
    {
        // Sin esto tiende a abrir una entrada por página en vez de agrupar las 2-5 de la declaración.
        var prompt = DocumentOcrPrompts.ClassificationPrompt(["aduana"]);

        prompt.Should().Contain("LOTE de 30 a 50");
        prompt.Should().Contain("Agrupalas TODAS en una sola entrada");
    }

    // ── Cargue individual sobre un expediente completo ───────────────────────
    // El operador puede soltar el expediente entero (31 págs) en la casilla de un tipo. El prompt debe
    // localizar sus páginas, no rechazar el archivo por contener además un FUR y una declaración.

    [Fact]
    public void Todos_los_prompts_acotan_las_validaciones_a_las_paginas_identificadas()
    {
        foreach (var tipo in DocumentOcrPrompts.SupportedTipos)
        {
            var prompt = DocumentOcrPrompts.PromptFor(tipo)!;

            prompt.Should().Contain("ALCANCE DE LAS VALIDACIONES");
            prompt.Should().Contain("NUNCA al archivo entero");
            // Sin esta frase el modelo lee «NO es valido si es un FUR» contra el expediente completo.
            prompt.Should().Contain("NO es motivo de rechazo");
        }
    }

    [Fact]
    public void Prompt_impronta_acepta_la_hoja_de_improntas_del_cliente()
    {
        // El prompt de clasificación ya la daba por válida y el de extracción la rechazaba por «no tener
        // origen en CDA/VUS/organismo»: la misma pieza salía Verificada en el lote y Rechazada suelta.
        var prompt = DocumentOcrPrompts.PromptFor("impronta")!;

        prompt.Should().Contain("hoja de improntas del\ncliente");
        prompt.Should().Contain("NO lleva sello de CDA");
        prompt.Should().NotContain("Para ser valido el documento debe tener origen en:");
    }

    [Fact]
    public void Los_dos_extremos_coinciden_sobre_la_impronta_del_cliente()
    {
        // Clasificador y extractor tienen que decir lo mismo, o la pieza llega propuesta y rechazada.
        DocumentOcrPrompts.ClassificationPrompt(["impronta"])
            .Should().Contain("improntas del cliente");
        DocumentOcrPrompts.PromptFor("impronta")!
            .Should().Contain("improntas del");
    }

    [Fact]
    public void El_clasificador_conoce_el_certificado_individual_de_aduanas_de_ensamblado_nacional()
    {
        // Los vehículos ensamblados en Colombia (Sofasa) no traen Declaración de Importación.
        DocumentOcrPrompts.ClassificationPrompt(["aduana"])
            .Should().Contain("ENSAMBLADO en Colombia");
    }
}
