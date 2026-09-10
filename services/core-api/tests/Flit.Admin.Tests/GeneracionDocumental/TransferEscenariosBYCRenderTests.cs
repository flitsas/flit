using System.Text.RegularExpressions;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using QuestPDF;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12208 — el PDF de los escenarios <b>B</b> (unilateral de leasing, art. 5.3.2.2) y <b>C</b>
/// (financiera a tercero, art. 5.3.2.1 sin exenciones) frente al anexo normativo
/// (<c>docs/plantilla-transferencia-dominio.md</c> §8.2, §8.3, §9.2, §9.3, §10 y §13).
///
/// <para><b>La verificación del escenario B es TEXTUAL, no visual.</b> Lo exige §9.2: «la
/// comprobación se hace extrayendo el texto del PDF y buscando esos dos literales; una inspección
/// visual del layout no satisface este requisito». Y está <b>acotada al bloque de firmas</b>: el
/// nombre del locatario sí aparece —y debe aparecer— en las cláusulas declarativas.</para>
///
/// <para><b>Por qué la comparación de los rótulos respeta mayúsculas.</b> Lo prohibido es el rótulo
/// <c>ADQUIRENTE</c> / <c>LOCATARIO</c>. La palabra «locatario» en minúscula aparece en la nota de
/// firmas del propio anexo («el locatario no firma el Formato Único»), de modo que una comparación
/// insensible a mayúsculas haría imposible cumplir §8.2 y §13.2 a la vez.</para>
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var pdf = new StandaloneTransferDocumentGenerator().Render(TransferTestData.ModeloEscenarioB());
/// var bloque = PdfTextExtractor.SignatureBlock(PdfTextExtractor.Extract(pdf.Content));
/// </code>
/// </summary>
public sealed class TransferEscenariosBYCRenderTests
{
    private static readonly StandaloneTransferDocumentGenerator Generator = new();

    static TransferEscenariosBYCRenderTests()
    {
        Settings.License = LicenseType.Community;
    }

    private static string TextoDe(TransferDocumentModel model) =>
        PdfTextExtractor.Extract(Generator.Render(model).Content);

    private static void DebeContener(string texto, string literal) =>
        PdfTextExtractor.Contains(texto, literal).Should().BeTrue(
            "el PDF debe contener \"{0}\"", literal);

    private static void NoDebeContener(string texto, string literal) =>
        PdfTextExtractor.Contains(texto, literal).Should().BeFalse(
            "el PDF no puede contener \"{0}\"", literal);

    // ── Escenario B — la trampa central: el bloque del adquirente NO SE INSTANCIA ──────────────

    /// <summary>
    /// <b>Verificación obligatoria del anexo §9.2 y §13.2, y criterio de aceptación literal.</b>
    /// Extraído el texto del PDF del escenario B, el bloque de firmas no contiene el rótulo
    /// <c>ADQUIRENTE</c> ni el rótulo <c>LOCATARIO</c>, y no existe ningún espacio de firma adicional
    /// al de la entidad financiera.
    /// </summary>
    [Fact]
    public void EscenarioB_ElBloqueDeFirmasNoMencionaAlAdquirenteNiAlLocatario()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());
        var bloque = PdfTextExtractor.SignatureBlock(texto);

        // Control positivo: el bloque recortado existe y trae al único firmante. Sin esto, un
        // recorte vacío «demostraría» cualquier ausencia.
        PdfTextExtractor.ContainsLiteral(bloque, TransferPartyRole.EtiquetaEntidadFinanciera)
            .Should().BeTrue("el bloque de firmas debe rotular a la entidad financiera");
        PdfTextExtractor.ContainsLiteral(bloque, "EMPRESA TRANSFERENTE SAS").Should().BeTrue();

        PdfTextExtractor.ContainsLiteral(bloque, "ADQUIRENTE").Should().BeFalse(
            "el rótulo ADQUIRENTE no puede existir en el bloque de firmas del escenario B (§9.2)");
        PdfTextExtractor.ContainsLiteral(bloque, "LOCATARIO").Should().BeFalse(
            "el rótulo LOCATARIO no puede existir en el bloque de firmas del escenario B (§9.2)");

        // Ni el nombre ni el documento del locatario aparecen en el bloque de firmas (§13.3).
        PdfTextExtractor.Contains(bloque, "COMPANIA DESTINATARIA DE PRUEBA SAS").Should().BeFalse();
        PdfTextExtractor.Contains(bloque, "901555444").Should().BeFalse();

        // UN solo espacio de firma. Dos rayas significarían el hueco que §10 regla #4 prohíbe.
        Regex.Matches(bloque, "_{10,}").Should().HaveCount(1);
    }

    /// <summary>
    /// El rótulo prohibido tampoco existe en ninguna otra parte del documento: no hay una columna
    /// «ADQUIRENTE» perdida en una cláusula ni una tabla con celda en blanco a su nombre.
    /// </summary>
    [Fact]
    public void EscenarioB_NingunRotuloDeAdquirenteNiLocatarioEnTodoElDocumento()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());

        PdfTextExtractor.ContainsLiteral(texto, "ADQUIRENTE").Should().BeFalse();
        PdfTextExtractor.ContainsLiteral(texto, "LOCATARIO").Should().BeFalse();
        PdfTextExtractor.ContainsLiteral(texto, "EL ADQUIRENTE").Should().BeFalse();

        // Y ninguna de las formulaciones prohibidas de §9.2 se cuela como texto.
        NoDebeContener(texto, "no requiere firma");
        NoDebeContener(texto, "no firma este documento");
        NoDebeContener(texto, "espacio reservado");
    }

    /// <summary>
    /// El locatario SÍ aparece en las cláusulas declarativas —segunda y tercera—: es lo que el anexo
    /// §8.2 exige. La prohibición es del bloque de firmas, no del documento.
    /// </summary>
    [Fact]
    public void EscenarioB_ElLocatarioApareceEnLasClausulasDeclarativas()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());

        DebeContener(texto, "SEGUNDA — ANTECEDENTE LEASING Y FUNDAMENTO DE LA TRANSFERENCIA");
        DebeContener(texto, "COMPANIA DESTINATARIA DE PRUEBA SAS");
        DebeContener(texto, "NIT No. 901555444");
        DebeContener(texto, "LSG-2020-000123");
    }

    /// <summary>
    /// La regla de §9.2 no depende del modo de firma (§9.0.3): el modelo se construye con una sola
    /// parte y el bloque del locatario no puede aparecer sea cual sea la cascada de firma. Aquí se
    /// comprueba componiendo el bloque directamente con la lista de una parte del escenario B.
    /// </summary>
    [Fact]
    public void EscenarioB_LaCascadaDeFirmaNoPuedeIntroducirUnHuecoEnBlanco()
    {
        var modelo = TransferTestData.ModeloEscenarioB();

        modelo.Partes.Should().ContainSingle();
        modelo.ParteConRol(TransferPartyRole.Adquirente).Should().BeNull();

        var texto = TextoDe(modelo);
        var bloque = PdfTextExtractor.SignatureBlock(texto);

        // Exactamente una línea de firma en todo el documento.
        Regex.Matches(PdfTextExtractor.Flatten(texto), "_{10,}").Should().HaveCount(1);
        Regex.Matches(bloque, "_{10,}").Should().HaveCount(1);
    }

    /// <summary>
    /// Un modelo de escenario B con dos partes es un error de programación, no un documento con un
    /// bloque de más: el generador falla antes de emitirlo (§10 regla #4).
    /// </summary>
    [Fact]
    public void EscenarioBConDosPartes_NoSeGenera()
    {
        var modelo = TransferTestData.ModeloEscenarioB() with
        {
            Partes =
            [
                .. TransferTestData.ModeloEscenarioB().Partes,
                new TransferDocumentParty(
                    TransferPartyRole.Adquirente, "PERSONA ADQUIRENTE DE PRUEBA", "CC", "10000002"),
            ],
        };

        var accion = () => Generator.Render(modelo);

        accion.Should().Throw<InvalidOperationException>()
            .WithMessage("*una sola parte*");
    }

    /// <summary>VB-B-05 en la plantilla: un escenario B con negocio no se emite.</summary>
    [Fact]
    public void EscenarioBConNegocio_NoSeGenera()
    {
        var modelo = TransferTestData.ModeloEscenarioB() with
        {
            Negocio = TransferTestData.Modelo().Negocio,
        };

        var accion = () => Generator.Render(modelo);

        accion.Should().Throw<InvalidOperationException>().WithMessage("*VB-B-05*");
    }

    /// <summary>Checklist §13.3 — el escenario B no imprime precio por ninguna vía.</summary>
    [Fact]
    public void EscenarioB_NoImprimePrecioNiContraprestacion()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());

        NoDebeContener(texto, "precio");
        NoDebeContener(texto, "contraprestación");
        // "COP" a secas daría un falso positivo con "copia del contrato de leasing" al comparar sin
        // espacios: se busca la forma en que el importe se imprime realmente ("($ 20.000.000 COP)").
        NoDebeContener(texto, "COP)");
        NoDebeContener(texto, "$ ");
        NoDebeContener(texto, "forma de pago");
    }

    /// <summary>
    /// Checklist §13.3 — la cláusula cuarta enumera las cinco exenciones del art. 5.3.2.2 con su
    /// numeración, y la nota aclara que la ausencia de firma del destinatario es una decisión de
    /// diseño del instrumento, no una exención adicional de la norma.
    /// </summary>
    [Fact]
    public void EscenarioB_EnumeraLasCincoExencionesYAclaraLaDecisionDeDiseno()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());

        DebeContener(texto, "CUARTA — EXENCIONES NORMATIVAS APLICABLES AL TRÁMITE (art. 5.3.2.2)");
        DebeContener(texto, "(1.ª) revisión técnico-mecánica");
        DebeContener(texto, "(2.ª) presentación de imagen del código QR");
        DebeContener(texto, "(3.ª) validación de paz y salvo");
        DebeContener(texto, "(4.ª) presentación del locatario");
        DebeContener(texto, "(5.ª) firma del Formato Único de Solicitud de Trámite");
        DebeContener(texto, "es una decisión de diseño del instrumento, no una exención expresa");

        // El hallazgo del dictamen (§6.3): el art. 5.3.2.2 NO exime lo fiscal.
        DebeContener(texto, "no exime el numeral 5.º del art. 5.3.2.1");
    }

    /// <summary>
    /// Parágrafo 1.º del art. 5.3.2.2: con opción automática basta el contrato; con las otras dos
    /// causales se anuncia también la declaración de terminación o de ejercicio de la opción.
    /// </summary>
    [Theory]
    [InlineData(TransferPurchaseOption.Automatica, false)]
    [InlineData(TransferPurchaseOption.Ejercida, true)]
    [InlineData(TransferPurchaseOption.TerminacionContrato, true)]
    public void EscenarioB_AnunciaLosSoportesSegunLaCausal(string opcion, bool conDeclaracion)
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB(tipoOpcion: opcion));

        DebeContener(texto, "QUINTA — SOPORTES APORTADOS");
        DebeContener(texto, "copia del contrato de leasing");

        if (conDeclaracion)
        {
            DebeContener(texto, "declaración de terminación del contrato o de ejercicio de la opción");
        }
        else
        {
            DebeContener(texto, "no exige declaración adicional");
        }
    }

    /// <summary>§9.0 — modo MANUSCRITA también en B: línea de firma, ninguna leyenda ni sello.</summary>
    [Fact]
    public void EscenarioB_NoContieneLeyendaDeFirmaElectronicaNiSello()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());

        DebeContener(texto, "_______________________________");

        foreach (var prohibido in new[]
        {
            "firma electr", "firmado electr", "firma digital", "sello de", "sello del", "sellado",
            "baúl", "baul", "validación de identidad", "validacion de identidad",
            "certificado de firma", "certificate_hash", "hash:", "cód. verificación", "Ley 527",
        })
        {
            NoDebeContener(texto, prohibido);
        }
    }

    /// <summary>Checklist §13.1 — ni variables ni etiquetas de renderización sin resolver.</summary>
    [Fact]
    public void EscenarioB_NoDejaVariablesNiEtiquetasSinResolver()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioB());

        NoDebeContener(texto, "{{");
        NoDebeContener(texto, "}}");
        NoDebeContener(texto, "[Solo si");
        NoDebeContener(texto, "[renderizar");
        DebeContener(texto, "Escenario: B — Transferencia unilateral leasing (art. 5.3.2.2)");
    }

    // ── Escenario C — no hereda exenciones (VB-C-07) ───────────────────────────────────────────

    /// <summary>
    /// VB-C-07 — el PDF del escenario C no invoca ninguna exención del art. 5.3.2.2, y su cláusula
    /// quinta enumera los requisitos plenos del art. 5.3.2.1 (§13.4).
    /// </summary>
    [Fact]
    public void EscenarioC_NoInvocaNingunaExencionDelArticulo5322()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioC());

        DebeContener(texto, "Escenario: C — Transferencia a tercero");
        DebeContener(texto, "SEGUNDA — ANTECEDENTE DE DOMINIO Y RÉGIMEN APLICABLE");
        DebeContener(texto, "sin que apliquen las exenciones previstas en el art. 5.3.2.2");
        DebeContener(texto, "QUINTA — REQUISITOS PLENOS DEL TRÁMITE");
        DebeContener(texto, "revisión técnico-mecánica vigente");
        DebeContener(texto, "código QR, certificación de guarismos o improntas");
        DebeContener(texto, "SIMIT");

        // Ninguna marca de concesión de exenciones.
        NoDebeContener(texto, "EXENCIONES NORMATIVAS APLICABLES");
        NoDebeContener(texto, "en este trámite no se exigen");
        NoDebeContener(texto, "excepción 1.ª");
        NoDebeContener(texto, "excepción 5.ª");
    }

    /// <summary>§9.3 — bloque bilateral con los rótulos del anexo y la nota de régimen.</summary>
    [Fact]
    public void EscenarioC_TieneBloqueBilateralConLaNotaDeRegimen()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioC());
        var bloque = PdfTextExtractor.SignatureBlock(texto);

        PdfTextExtractor.ContainsLiteral(bloque, TransferPartyRole.EtiquetaTransferenteFinanciero)
            .Should().BeTrue();
        PdfTextExtractor.ContainsLiteral(bloque, TransferPartyRole.EtiquetaAdquirenteTercero)
            .Should().BeTrue();

        // Dos espacios de firma: aquí sí comparecen dos partes.
        Regex.Matches(bloque, "_{10,}").Should().HaveCount(2);

        PdfTextExtractor.Contains(bloque, "Rige íntegramente el art. 5.3.2.1").Should().BeTrue();
        PdfTextExtractor.Contains(bloque, "No aplican las exenciones del art. 5.3.2.2").Should().BeTrue();
    }

    /// <summary>
    /// VB-C-07 como verificación de plantilla: si una edición futura pegara en el escenario C la
    /// cláusula de exenciones del escenario B, la composición falla citando el código en vez de
    /// emitir un documento que prometa al tercero exenciones que no tiene.
    /// </summary>
    [Theory]
    [InlineData("CUARTA — EXENCIONES NORMATIVAS APLICABLES AL TRÁMITE (art. 5.3.2.2)")]
    [InlineData("De conformidad con el art. 5.3.2.2, en este trámite no se exigen: RTM ni improntas.")]
    [InlineData("Firma del Formato Único por parte del locatario (excepción 5.ª).")]
    public void UnaPlantillaDeEscenarioCConClausulaDeExenciones_FallaConVbC07(string parrafo)
    {
        var accion = () => TransferEscenarioC.VerificarSinExenciones([parrafo]);

        accion.Should().Throw<InvalidOperationException>().WithMessage("*VB-C-07*");
    }

    /// <summary>La mención de que el art. 5.3.2.2 NO aplica es lo contrario de invocarlo: pasa.</summary>
    [Fact]
    public void LaMencionDeInaplicabilidadDelArticulo5322_NoFallaLaVerificacion()
    {
        var accion = () => TransferEscenarioC.VerificarSinExenciones(
        [
            "La presente operación se rige íntegramente por el art. 5.3.2.1, sin que apliquen las "
            + "exenciones previstas en el art. 5.3.2.2.",
        ]);

        accion.Should().NotThrow();
    }

    /// <summary>§9.0 — modo MANUSCRITA también en C.</summary>
    [Fact]
    public void EscenarioC_NoContieneLeyendaDeFirmaElectronicaNiSello()
    {
        var texto = TextoDe(TransferTestData.ModeloEscenarioC());

        // "sello" a secas daría un falso positivo al comparar sin espacios: "no es el locatario" se
        // aplana a "noesellocatario", que contiene "sello". Se buscan las formas en que un sello
        // aparecería de verdad.
        foreach (var prohibido in new[]
        {
            "firma electr", "firma digital", "sello de", "sello del", "sellado", "baúl", "baul",
            "validación de identidad", "certificate_hash", "hash:",
        })
        {
            NoDebeContener(texto, prohibido);
        }
    }

    /// <summary>Checklist §13.1 — los 13 campos del vehículo también en B y C.</summary>
    [Theory]
    [InlineData("B")]
    [InlineData("C")]
    public void AmbosEscenariosImprimenLosTreceCamposDelVehiculo(string escenario)
    {
        var modelo = escenario == TransferScenario.UnilateralLeasing
            ? TransferTestData.ModeloEscenarioB()
            : TransferTestData.ModeloEscenarioC();

        var texto = TextoDe(modelo);

        foreach (var etiqueta in new[]
        {
            "Placa", "Marca", "Línea", "Año modelo", "Clase", "Carrocería", "Color(es)",
            "Motor No.", "Chasis / VIN No.", "Serie No.", "Servicio", "Licencia de Tránsito No.",
            "Organismo de Tránsito",
        })
        {
            DebeContener(texto, etiqueta);
        }
    }
}
