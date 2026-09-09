using System.Text.RegularExpressions;
using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Infrastructure.Documents.Standalone;
using FluentAssertions;
using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Flit.Admin.Tests.GeneracionDocumental;

/// <summary>
/// HU #12207 — el PDF del escenario A frente al checklist del anexo normativo
/// (<c>docs/plantilla-transferencia-dominio.md</c> §13).
///
/// <para>Las comprobaciones de firma son <b>textuales</b>, sobre el texto extraído del PDF, y no
/// visuales: lo exige §13.2 y es la única forma de demostrar que no se coló una leyenda de firma
/// electrónica o un sello. Se comparan sin espacios porque Skia escribe el PDF glifo a glifo
/// (ver <see cref="PdfTextExtractor.Flatten"/>).</para>
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// var pdf = new StandaloneTransferDocumentGenerator().Render(TransferTestData.Modelo());
/// var texto = PdfTextExtractor.Extract(pdf.Content);
/// PdfTextExtractor.Contains(texto, "COMPARECIENTES"); // true
/// </code>
/// </summary>
public sealed class TransferScenarioRenderTests
{
    private static readonly StandaloneTransferDocumentGenerator Generator = new();

    static TransferScenarioRenderTests()
    {
        // Los dos tests que componen un documento suelto (sin pasar por el generador) necesitan la
        // licencia fijada aquí: el constructor estático del generador podría no haber corrido.
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

    [Fact]
    public void EscenarioA_ProduceUnPdfConEncabezadoClausulasYFirmas()
    {
        var rendered = Generator.Render(TransferTestData.Modelo());

        rendered.Mimetype.Should().Be("application/pdf");
        rendered.Filename.Should().StartWith("transferencia_dominio_ABC123_");
        System.Text.Encoding.ASCII.GetString(rendered.Content, 0, 4).Should().Be("%PDF");

        var texto = PdfTextExtractor.Extract(rendered.Content);

        // Control positivo del extractor: si esto falla, el resto de aserciones textuales no
        // significan nada (un extractor mudo «demuestra» cualquier ausencia).
        DebeContener(texto, "DOCUMENTO DE TRANSFERENCIA DE DOMINIO DE VEHÍCULO AUTOMOTOR");

        DebeContener(texto, "ABC123");
        DebeContener(texto, "Escenario: A — Traspaso ordinario (art. 5.3.2.1)");
        DebeContener(texto, "COMPARECIENTES");
        DebeContener(texto, "PRIMERA — IDENTIFICACIÓN DEL VEHÍCULO");
        DebeContener(texto, "SEGUNDA — TRANSFERENCIA DE DOMINIO");
        DebeContener(texto, "TERCERA — TRADICIÓN Y ENTREGA");
        DebeContener(texto, "CUARTA — DECLARACIONES DEL TRANSFERENTE");
        DebeContener(texto, "QUINTA — OBLIGACIONES REGISTRALES");
        DebeContener(texto, "SEXTA — RETENCIÓN EN LA FUENTE, IMPUESTOS Y DERECHOS DEL TRÁMITE");

        // Bloque de firmas bilateral (anexo §9.1 y checklist §13.2).
        DebeContener(texto, "TRANSFERENTE");
        DebeContener(texto, "ADQUIRENTE");
        DebeContener(texto, "EMPRESA TRANSFERENTE SAS");
        DebeContener(texto, "PERSONA ADQUIRENTE DE PRUEBA");

        // El DV del NIT viaja resuelto, no como variable.
        DebeContener(texto, "NIT No. 900123456, DV 8");
    }

    /// <summary>
    /// Adenda 15.4 y anexo §9.0 / §13.2 — el modo de firma es MANUSCRITA: líneas de firma, y NINGUNA
    /// leyenda de firma electrónica ni sello, aunque la parte tenga firma vigente en el baúl. El
    /// generador ni siquiera tiene un puerto para consultarlo.
    /// </summary>
    [Fact]
    public void EscenarioA_NoContieneLeyendaDeFirmaElectronicaNiSello()
    {
        var texto = TextoDe(TransferTestData.Modelo());

        DebeContener(texto, "_______________________________");

        foreach (var prohibido in new[]
        {
            "firma electr",          // «firma electrónica» / «firma electronica»
            "firmado electr",
            "firma digital",
            "sello",
            "baúl", "baul",
            "validación de identidad", "validacion de identidad",
            "certificado de firma",
            "certificate_hash",
            "hash:",
            "cód. verificación", "cod. verificacion",
            "Ley 527",
        })
        {
            NoDebeContener(texto, prohibido);
        }
    }

    /// <summary>
    /// Checklist §13.1 — ninguna variable sin resolver y ninguna etiqueta de renderización
    /// condicional en el PDF final.
    /// </summary>
    [Fact]
    public void EscenarioA_NoDejaVariablesNiEtiquetasDeRenderizacionSinResolver()
    {
        var texto = TextoDe(TransferTestData.Modelo());

        NoDebeContener(texto, "{{");
        NoDebeContener(texto, "}}");
        NoDebeContener(texto, "[Solo si");
        NoDebeContener(texto, "[renderizar");
    }

    /// <summary>Checklist §13.1 — la tabla de vehículo lleva los 13 campos del anexo §5.1.</summary>
    [Fact]
    public void EscenarioA_ImprimeLosTreceCamposDelVehiculo()
    {
        var texto = TextoDe(TransferTestData.Modelo());

        foreach (var etiqueta in new[]
        {
            "Placa", "Marca", "Línea", "Año modelo", "Clase", "Carrocería", "Color(es)",
            "Motor No.", "Chasis / VIN No.", "Serie No.", "Servicio", "Licencia de Tránsito No.",
            "Organismo de Tránsito",
        })
        {
            DebeContener(texto, etiqueta);
        }

        // Y los valores, no solo los rótulos.
        DebeContener(texto, "MOT0000001");
        DebeContener(texto, "CHA0000001");
        DebeContener(texto, "11223344");
    }

    /// <summary>
    /// Anexo §8.1, cláusula SEXTA — las tres variables fiscales de §5.4 llegan al PDF. Sin esta
    /// cláusula se capturarían en el formulario y nunca saldrían impresas.
    /// </summary>
    [Fact]
    public void EscenarioA_ImprimeLasTresVariablesFiscalesDeLaClausulaSexta()
    {
        var texto = TextoDe(TransferTestData.Modelo());

        DebeContener(texto, "la retención en la fuente será asumida por EL TRANSFERENTE");
        DebeContener(texto, "el impuesto sobre vehículos automotores por EL ADQUIRENTE");
        DebeContener(
            texto,
            "los derechos del Organismo de Tránsito por ambas partes en proporciones iguales");
    }

    /// <summary>
    /// Anexo §8.1 — en remolques y semirremolques se OMITE quién asume el impuesto (están exentos) y
    /// se agrega el inciso de la Ley 488/1998.
    /// </summary>
    [Fact]
    public void Remolque_OmiteLaAsuncionDelImpuestoYCitaLaExencionCorrecta()
    {
        var modelo = TransferTestData.Modelo(clase: "SEMIRREMOLQUE");
        var sinImpuesto = modelo with
        {
            Negocio = modelo.Negocio! with { AsumeImpuestoVehiculo = null },
        };

        var texto = TextoDe(sinImpuesto);

        NoDebeContener(texto, "el impuesto sobre vehículos automotores por EL");
        DebeContener(
            texto,
            "no verifica el pago del impuesto sobre vehículos, por encontrarse exento conforme a la "
            + "Ley 488 de 1998");

        // La exención de la Ley 488/1998 es de IMPUESTO, no de SOAT: el anexo corrigió esa
        // atribución (§4.1 y §6.2) y el PDF no puede reintroducirla.
        NoDebeContener(texto, "SOAT, por encontrarse exento");
        NoDebeContener(texto, "SOAT conforme a la Ley 488");
        NoDebeContener(texto, "exento de SOAT");
    }

    /// <summary>
    /// La estructura de firmas la decide el ESCENARIO, no el modo de firma (anexo §9.0.3). Este test
    /// congela esa propiedad en el bloque reutilizable: con un firmante se pinta UN bloque y el
    /// rótulo del adquirente no aparece por ninguna parte. Es la garantía sobre la que HU-06 monta el
    /// escenario B, donde ese bloque no debe existir ni vacío, ni oculto, ni suprimido en render.
    /// </summary>
    [Fact]
    public void UnSoloFirmante_NoProduceRotuloNiHuecoDelAdquirente()
    {
        var modelo = TransferTestData.Modelo();
        var soloTransferente = modelo.ParteConRol(TransferPartyRole.Transferente)! with
        {
            RolEtiqueta = "LA ENTIDAD FINANCIERA",
        };

        var pdf = Document.Create(doc => doc.Page(page =>
        {
            page.Margin(30);
            page.Content().Column(col => TransferDocumentBlocks.Firmas(
                col, modelo.CiudadFirma, modelo.FechaFirma, [soloTransferente]));
        })).GeneratePdf();

        var texto = PdfTextExtractor.Extract(pdf);

        DebeContener(texto, "LA ENTIDAD FINANCIERA");
        NoDebeContener(texto, "ADQUIRENTE");
        NoDebeContener(texto, "LOCATARIO");

        // Un solo espacio de firma: dos rayas significarían un hueco de firma sobrante.
        Regex.Matches(PdfTextExtractor.Flatten(texto), "_{10,}").Should().HaveCount(1);
    }

    /// <summary>Lista de firmantes vacía: es un error de programación, no un documento sin firmas.</summary>
    [Fact]
    public void SinFirmantes_ElBloqueDeFirmasFalla()
    {
        var accion = () => Document.Create(doc => doc.Page(page =>
        {
            page.Margin(30);
            page.Content().Column(col => TransferDocumentBlocks.Firmas(
                col, "CIUDAD DE PRUEBA", new DateOnly(2026, 9, 9), []));
        })).GeneratePdf();

        accion.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Un escenario fuera del catálogo debe fallar de forma explícita y no producir un PDF a
    /// medias. (En HU #12207 este caso cubría a B y C; desde HU #12208 ambos se componen y sus
    /// pruebas viven en <c>TransferEscenariosBYCRenderTests</c>.)
    /// </summary>
    [Theory]
    [InlineData("Z")]
    [InlineData("")]
    public void EscenarioFueraDelCatalogo_NoSeCompone(string escenario)
    {
        var accion = () => Generator.Render(TransferTestData.Modelo() with { Scenario = escenario });

        accion.Should().Throw<NotSupportedException>();
    }

    /// <summary>
    /// El modo de firma no es un parámetro abierto: <c>ESTAMPADA</c> está diferido (§9.4) y exige ADR
    /// previo sobre la evidencia de consentimiento. Emitir con un modo no dictaminado sería peor que
    /// no emitir.
    /// </summary>
    [Fact]
    public void ModoDeFirmaEstampada_NoSeGenera()
    {
        var accion = () => Generator.Render(TransferTestData.Modelo() with { SignatureMode = "ESTAMPADA" });

        accion.Should().Throw<NotSupportedException>();
    }
}
