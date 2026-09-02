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
    [InlineData("tarjeta_propiedad")] // HU #11996
    [InlineData("paz_salvo")] // HU #11998
    [InlineData("inscripcion_prenda")] // HU #11999
    [InlineData("comprobante_derechos")] // HU #12000
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

    // ── HU #11996 — prompt de la licencia de tránsito ────────────────────────
    //
    // Cada aserción de aquí abajo corresponde a un fallo REAL observado al calibrar contra 55
    // licencias de las 8 secretarías. No son comprobaciones de estilo: si una se cae, vuelve un
    // fallo que ya costó una iteración de prompt.

    [Fact]
    public void Prompt_licencia_prohibe_suponer_el_orden_de_las_caras()
    {
        // Medellín y Envigado traen el REVERSO en la página 1; Funza y Palmira el anverso; Sabaneta
        // mete las dos caras en una sola página. Suponer el orden se traduce en campos vacíos.
        var prompt = DocumentOcrPrompts.PromptFor("tarjeta_propiedad")!;
        prompt.Should().Contain("NO ASUMAS EL ORDEN");
        prompt.Should().Contain("LAS DOS CARAS EN UNA MISMA PAGINA");
    }

    [Fact]
    public void Prompt_licencia_distingue_los_dos_significados_del_asterisco()
    {
        // "*****" a solas es SIN DATO (motor y cilindrada de los eléctricos) y debe salir vacío;
        // el asterisco dentro del valor es troquelado y se conserva ("L4F*242904046*"). La BD de V1
        // guarda esa misma convención, así que confundirlas rompe el cruce.
        var prompt = DocumentOcrPrompts.PromptFor("tarjeta_propiedad")!;
        prompt.Should().Contain("ASTERISCOS");
        prompt.Should().Contain("L4F*242904046*");
        prompt.Should().Contain("CADENA VACIA");
    }

    [Fact]
    public void Prompt_licencia_obliga_a_contar_los_caracteres_del_vin_y_de_la_placa()
    {
        // Enunciar "el VIN tiene 17 caracteres" no bastó: 7 de 11 VIN erróneos tenían otra longitud.
        // Lo que funcionó fue ordenar el acto de contar. Igual con la placa, que subió de 88,7 % a
        // 92,7 % al pedir contar y verificar el patrón en vez de solo describirlo.
        var prompt = DocumentOcrPrompts.PromptFor("tarjeta_propiedad")!;
        prompt.Should().Contain("CUENTALOS");
        prompt.Should().Contain("EXACTAMENTE 17 caracteres");
        prompt.Should().Contain("CUENTA los caracteres");
    }

    [Fact]
    public void Prompt_licencia_no_confunde_la_placa_con_el_serial_del_reverso()
    {
        // El escaneo de Medellín hizo que el modelo devolviera "LT10099922" como placa: es el serial
        // de la especie venal del reverso.
        var prompt = DocumentOcrPrompts.PromptFor("tarjeta_propiedad")!;
        prompt.Should().Contain("serial_especie_venal");
        // Los dos valores reales que el modelo devolvió como placa antes de la corrección.
        prompt.Should().Contain("LT10099922");
        prompt.Should().Contain("son el serial del reverso, NO placas");
    }

    [Fact]
    public void Prompt_licencia_rechaza_el_recibo_sin_rechazar_el_expediente_que_la_contiene()
    {
        // Las dos reglas se contradicen si no se reconcilian: al endurecer el rechazo de recibos
        // aparecieron 2 falsos rechazos de expedientes que SÍ traían la tarjeta en páginas posteriores.
        var prompt = DocumentOcrPrompts.PromptFor("tarjeta_propiedad")!;
        prompt.Should().Contain("Especie Venal Lic.Tto.");
        prompt.Should().Contain("ESTO NO SIGNIFICA RECHAZAR EL ARCHIVO");
        prompt.Should().Contain("NUNCA rechaces un archivo por lo que contienen las paginas que NO son la licencia");
    }

    [Fact]
    public void Prompt_licencia_pide_los_campos_que_pinta_el_resumen_del_checklist()
    {
        // Contrato implícito con OCR_RESUMEN_FIELDS del front: si un nombre cambia aquí, la tarjeta
        // del checklist se queda con la fila en blanco y nadie se entera hasta producción.
        var prompt = DocumentOcrPrompts.PromptFor("tarjeta_propiedad")!;
        foreach (var campo in new[]
                 {
                     "vehiculo_placa", "numero_licencia", "vehiculo_marca", "vehiculo_linea",
                     "vehiculo_modelo", "vehiculo_color", "vehiculo_servicio", "vehiculo_motor",
                     "propietario_nombre", "propietario_tipo_documento", "propietario_documento",
                     "organismo_transito", "fecha_expedicion", "vehiculo_vin",
                 })
        {
            prompt.Should().Contain(campo);
        }
    }

    // ── HU #11998 — prompt del paz y salvo de impuestos ──────────────────────
    //
    // El paz y salvo no es un artefacto con formato fijo: es un requisito que cada departamento
    // acredita distinto. Estas aserciones fijan las decisiones que hicieron que el prompt funcione.

    [Fact]
    public void Prompt_paz_salvo_decide_por_el_emisor_y_no_por_el_formato()
    {
        // Ninguno de los 43 ejemplares con capa de texto contenía la frase "PAZ Y SALVO": buscarla
        // como criterio habría rechazado la muestra entera.
        var prompt = DocumentOcrPrompts.PromptFor("paz_salvo")!;
        prompt.Should().Contain("LA CLAVE ES QUIEN LO EMITE");
        prompt.Should().Contain("AUTORIDAD TRIBUTARIA");
    }

    [Fact]
    public void Prompt_paz_salvo_acepta_las_tres_formas_en_que_se_acredita()
    {
        // Estado de cuenta (Antioquia, Caldas), histórico del portal (Cundinamarca) y declaración
        // (Valle, Santander). Aceptar solo la primera rechazaría ~70 % de lo que se sube.
        var prompt = DocumentOcrPrompts.PromptFor("paz_salvo")!;
        prompt.Should().Contain("ESTADO DE CUENTA");
        prompt.Should().Contain("HISTORICO DE PAGOS");
        prompt.Should().Contain("DECLARACION del impuesto");
    }

    [Fact]
    public void Prompt_paz_salvo_rechaza_lo_que_mas_se_le_parece()
    {
        // Los dos falsos amigos, que suman 12 de 54 en la muestra: el comprobante PSE acredita una
        // transacción y no un estado de cuenta; el recibo de caja de la secretaría cobra derechos de
        // trámite y ni siquiera es del impuesto vehicular, aunque hable del SIMIT (que son multas).
        var prompt = DocumentOcrPrompts.PromptFor("paz_salvo")!;
        prompt.Should().Contain("Pago PSE");
        prompt.Should().Contain("Derechos de Sistematizacion");
        prompt.Should().Contain("SIMIT son MULTAS");
    }

    [Fact]
    public void Prompt_paz_salvo_no_supone_que_esta_al_dia_por_existir()
    {
        // El sesgo obvio del modelo es dar por bueno cualquier documento que le pongan delante. Un
        // recuadro de vigencias VACÍO sí significa al día; que no haya recuadro, no.
        var prompt = DocumentOcrPrompts.PromptFor("paz_salvo")!;
        prompt.Should().Contain("NO deduzcas \"al_dia\" solo porque el documento existe");
        prompt.Should().Contain("no_determinado");
    }

    [Fact]
    public void Prompt_paz_salvo_pide_los_campos_que_pinta_el_resumen_del_checklist()
    {
        var prompt = DocumentOcrPrompts.PromptFor("paz_salvo")!;
        foreach (var campo in new[]
                 {
                     "estado_deuda", "vigencias_adeudadas", "emisor", "numero_certificado",
                     "vehiculo_placa", "propietario_nombre", "municipio", "departamento",
                     "vigencia_certificada", "fecha_expedicion",
                 })
        {
            prompt.Should().Contain(campo);
        }
    }

    // ── HU #11999 — inscripción de prenda ────────────────────────────────────────────────────
    // Aquí el dato de valor no es el vehículo sino el acreedor. Estas aserciones fijan las dos
    // decisiones que costaron falsos rechazos reales en la primera versión del prompt.

    [Fact]
    public void Prompt_inscripcion_prenda_acepta_las_tres_formas_de_acreditar_la_garantia()
    {
        // Contrato de garantía mobiliaria (41 de 65), consulta RUNT del RNGM (9) y certificado del
        // RUG. Aceptar solo el contrato dejaría fuera a quien acredita con el registro ya hecho.
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        prompt.Should().Contain("CONTRATO DE GARANTIA MOBILIARIA");
        prompt.Should().Contain("RUG / RNGM");
        prompt.Should().Contain("CONSULTA DEL RUNT");
    }

    [Fact]
    public void Prompt_inscripcion_prenda_no_exige_el_registro_que_el_tramite_va_a_hacer()
    {
        // Falso rechazo real: el modelo pedía folio del RUG y fecha de inscripción. El trámite
        // existe precisamente para inscribir la prenda, así que el contrato llega ANTES de estar
        // registrado; exigir la constancia es pedir el resultado como requisito de entrada.
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        prompt.Should().Contain("LO QUE NO DEBES EXIGIR");
        prompt.Should().Contain("NO exijas numero de folio del RUG");
    }

    [Fact]
    public void Prompt_inscripcion_prenda_se_conforma_con_un_solo_identificador_del_vehiculo()
    {
        // El otro falso rechazo: 14 de los 50 documentos aceptados no traen placa, porque el
        // contrato identifica el vehículo por chasis. La placa vacía no puede invalidar nada.
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        prompt.Should().Contain("UNO SOLO BASTA");
        prompt.Should().Contain("la placa vacia NO invalida el documento");
    }

    [Fact]
    public void Prompt_inscripcion_prenda_busca_al_acreedor_en_el_cuerpo_y_no_en_la_firma()
    {
        // Quien firma al pie es el deudor o un apoderado. Acotar la búsqueda al cuerpo del
        // contrato subió el acierto del acreedor del 95 % al 100 % (40/40, en dos corridas).
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        prompt.Should().Contain("ACREEDOR GARANTIZADO");
        prompt.Should().Contain("no al pie");
    }

    [Fact]
    public void Prompt_inscripcion_prenda_rechaza_el_documento_en_blanco()
    {
        // 11 de los 65 documentos de la muestra eran una página en blanco, y 10 de ellos el MISMO
        // archivo byte a byte: un relleno para poder pasar el formulario de V1.
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        prompt.Should().Contain("PAGINA EN BLANCO");
        prompt.Should().Contain("documento_en_blanco");
    }

    [Fact]
    public void Prompt_inscripcion_prenda_corrige_la_placa_por_posicion()
    {
        // Las tres primeras posiciones son letras y las tres últimas dígitos, así que un 0 al
        // principio es una O. Eso arregló JR0888 → JRO888 sin tocar nada más.
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        prompt.Should().Contain("POSICION POR POSICION");
        prompt.Should().Contain("un 0 en las tres primeras posiciones es en realidad una O");
    }

    [Fact]
    public void Prompt_inscripcion_prenda_pide_los_campos_que_pinta_el_resumen_del_checklist()
    {
        var prompt = DocumentOcrPrompts.PromptFor("inscripcion_prenda")!;
        foreach (var campo in new[]
                 {
                     "acreedor_nombre", "acreedor_documento", "garante_nombre", "numero_registro",
                     "fecha_registro", "vehiculo_placa", "vehiculo_chasis", "vehiculo_vin",
                     "vehiculo_marca", "vehiculo_linea", "vehiculo_modelo",
                 })
        {
            prompt.Should().Contain(campo);
        }
    }

    // ── HU #12000 — comprobante de pago ──────────────────────────────────────────────────────
    // Una casilla con dos documentos dentro, y un criterio que se invierte respecto al paz y salvo.

    [Fact]
    public void Prompt_comprobante_derechos_acepta_las_tres_familias_de_la_casilla()
    {
        // Recibo de derechos del organismo (25 de 66), declaración del impuesto (33) y comprobante
        // electrónico (5). En matrícula la casilla es del impuesto y en traspaso de los derechos.
        var prompt = DocumentOcrPrompts.PromptFor("comprobante_derechos")!;
        prompt.Should().Contain("RECIBO DE CAJA");
        prompt.Should().Contain("COMPROBANTE DE PAGO ELECTRONICO O BANCARIO");
        prompt.Should().Contain("DECLARACION o LIQUIDACION DEL IMPUESTO");
    }

    [Fact]
    public void Prompt_comprobante_derechos_advierte_que_aqui_el_pago_PSE_si_vale()
    {
        // El paz y salvo rechaza el comprobante PSE porque acredita una transacción y no un estado
        // de cuenta. Aquí es exactamente lo que se pide, y hay que decirlo para que el modelo no
        // arrastre el criterio del documento vecino.
        var prompt = DocumentOcrPrompts.PromptFor("comprobante_derechos")!;
        prompt.Should().Contain("AQUI EL COMPROBANTE DE PAGO SI VALE");
        prompt.Should().Contain("A diferencia de un paz y salvo");
    }

    [Fact]
    public void Prompt_comprobante_derechos_no_rechaza_una_liquidacion_sin_pagar()
    {
        // El documento correcto sin cancelar sigue siendo el documento correcto. Eso se informa en
        // hay_constancia_pago, no en es_valido.
        var prompt = DocumentOcrPrompts.PromptFor("comprobante_derechos")!;
        prompt.Should().Contain("una liquidacion sin pagar sigue siendo el documento correcto");
        prompt.Should().Contain("hay_constancia_pago");
    }

    [Fact]
    public void Prompt_comprobante_derechos_extrae_el_valor_este_pagado_o_no()
    {
        // Falla real del v1: el campo se llamaba valor_pagado y el modelo lo tomó al pie de la letra,
        // dejando en 0 los 15 documentos aún no cancelados. Separar el valor del estado lo arregló.
        var prompt = DocumentOcrPrompts.PromptFor("comprobante_derechos")!;
        prompt.Should().Contain("EL VALOR VA SIEMPRE, ESTE PAGADO O NO");
        prompt.Should().Contain("valor_total");
        prompt.Should().NotContain("valor_pagado");
    }

    [Fact]
    public void Prompt_comprobante_derechos_rechaza_el_paz_y_salvo_y_la_licencia()
    {
        // Los dos rechazos reales de la muestra fueron licencias de tránsito cargadas en esta casilla.
        // El estado de cuenta se rechaza porque tiene casilla propia.
        var prompt = DocumentOcrPrompts.PromptFor("comprobante_derechos")!;
        prompt.Should().Contain("ESTADO DE CUENTA o PAZ Y SALVO");
        prompt.Should().Contain("LICENCIA DE TRANSITO");
    }

    [Fact]
    public void Prompt_comprobante_derechos_pide_los_campos_que_pinta_el_resumen_del_checklist()
    {
        var prompt = DocumentOcrPrompts.PromptFor("comprobante_derechos")!;
        foreach (var campo in new[]
                 {
                     "hay_constancia_pago", "valor_total", "entidad_recaudadora", "conceptos",
                     "numero_referencia", "fecha_pago", "vehiculo_placa", "propietario_nombre",
                     "municipio", "departamento",
                 })
        {
            prompt.Should().Contain(campo);
        }
    }
}
