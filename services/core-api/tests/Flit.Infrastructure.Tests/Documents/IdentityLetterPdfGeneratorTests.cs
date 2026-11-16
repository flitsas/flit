using System.Text;
using Flit.Admin.Tests.GeneracionDocumental;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Identity;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — <see cref="IdentityLetterPdfGenerator"/>: una hoja Carta con la marca FLIT, las
/// secciones del diseño y SIN QR, score, «Kyverum», «Ley 527», fecha de nacimiento, sexo ni el nombre del revisor. Verificación
/// TEXTUAL (extrayendo el texto del PDF). Datos ficticios. Con <c>FLIT_LETTER_SAMPLE_DIR</c> definida, deja un PDF de muestra.
/// </summary>
public sealed class IdentityLetterPdfGeneratorTests
{
    private static readonly DateTimeOffset Emitida = new(2026, 11, 5, 15, 30, 0, TimeSpan.Zero); // 10:30 en Colombia

    private static byte[] Jpeg(Rgba32 color, int w = 640, int h = 480)
    {
        using var image = new Image<Rgba32>(w, h, color);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    private static byte[] FirmaPng()
    {
        using var image = new Image<Rgba32>(300, 100, new Rgba32(0, 0, 0, 0));
        for (var x = 20; x < 280; x++)
        {
            var y = 50 + (int)(25 * Math.Sin(x / 14d));
            image[x, y] = new Rgba32(10, 20, 80, 255);
            image[x, y + 1] = new Rgba32(10, 20, 80, 255);
        }

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static IdentityLetterData Data(Action<List<string>>? _ = null) => new(
        Radicado: "CERT-0A1B2C3D4E5F",
        CompanyName: "Compañía de Prueba SAS",
        Reference: "41391d08-b747-423d-9994-53aa8e1c1b06",
        IssuedAt: Emitida,
        RoleLabel: "COMPRADOR",
        RegisteredName: "PERSONA DE PRUEBA UNO",
        DocumentType: "CC",
        DocumentNumber: "1000000001",
        ValidatedAt: Emitida.AddMinutes(-3),
        ValidUntil: new DateTimeOffset(2026, 12, 5, 0, 0, 0, TimeSpan.FromHours(-5)),
        Serie: "FL-3C9A71BE-04D5F2",
        EvidenceSha256: new string('a', 64),
        SealedAt: Emitida,
        ConsentIp: "203.0.113.7",
        ConsentAt: Emitida.AddMinutes(-20),
        ConsentTextVersion: "manual-ley1581-v2",
        Texts: ManualIdentityAuthorizationTexts.For("manual-ley1581-v2"),
        Selfie: Jpeg(new Rgba32(120, 160, 200)),
        IdFront: Jpeg(new Rgba32(200, 180, 120), 800, 500),
        IdBack: Jpeg(new Rgba32(160, 200, 140), 800, 500),
        Signature: FirmaPng());

    // El extractor devuelve las fuentes con kerning letra a letra («C E R T»): se compara sin espacios.
    private static string PdfText(byte[] pdf) => Compact(PdfTextExtractor.Extract(pdf));

    private static string Compact(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    [Fact]
    public void AC1_Genera_un_PDF_de_una_pagina_Carta_con_las_secciones_del_diseno()
    {
        var pdf = new IdentityLetterPdfGenerator().Generate(Data());

        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        System.Text.RegularExpressions.Regex.Count(Encoding.Latin1.GetString(pdf), "/Type */Page(?!s)").Should().Be(1, "una sola hoja Carta");
        var texto = PdfText(pdf);
        texto.Should().Contain(Compact("CERTIFICACIÓN DE VALIDACIÓN DE IDENTIDAD"));
        texto.Should().Contain(Compact("Verificación de identidad - FLIT"));
        texto.Should().Contain(Compact("Radicado: CERT-0A1B2C3D4E5F")).And.Contain(Compact("Titular 1 de 1"));
        texto.Should().Contain(Compact("SOLICITUD")).And.Contain(Compact("Proyecto: Compañía de Prueba SAS"));
        texto.Should().Contain(Compact("Referencia: 41391d08-b747-423d-9994-53aa8e1c1b06"));
        texto.Should().Contain(Compact("COMPRADOR")).And.Contain(Compact("VALIDADO"));
        texto.Should().Contain(Compact("Nombre registrado")).And.Contain(Compact("PERSONA DE PRUEBA UNO"));
        texto.Should().Contain(Compact("Tipo de documento")).And.Contain(Compact("Número de documento")).And.Contain(Compact("1000000001"));
        texto.Should().Contain(Compact("Método")).And.Contain(Compact("Revisión manual"));
        texto.Should().Contain(Compact("Resultado")).And.Contain(Compact("APROBADO"));
        texto.Should().Contain(Compact("Fecha de validación")).And.Contain(Compact("Vigencia")).And.Contain(Compact("Revisado por"));
        texto.Should().Contain(Compact("CÓDIGO DE INTEGRIDAD")).And.Contain(Compact("FL-3C9A71BE-04D5F2")).And.Contain(Compact(new string('a', 64)));
        texto.Should().Contain(Compact("Hash (SHA-256)")).And.Contain(Compact("Sellado"));
        texto.Should().Contain(Compact("TRAZABILIDAD Y CONSENTIMIENTO")).And.Contain(Compact("Dirección IP")).And.Contain(Compact("203.0.113.7"));
        texto.Should().Contain(Compact("Consentimiento Ley 1581")).And.Contain(Compact("versión manual-ley1581-v2"));
        texto.Should().Contain(Compact("Custodia de las imágenes")).And.Contain(Compact("FLIT conserva las imágenes"));
        texto.Should().Contain(Compact("EVIDENCIA CAPTURADA")).And.Contain(Compact("Selfie"))
            .And.Contain(Compact("Documento (frente)")).And.Contain(Compact("Documento (reverso)"));
        texto.Should().Contain(Compact("FIRMA Y AUTORIZACIÓN")).And.Contain(Compact("Firma manuscrita"));
        texto.Should().Contain(Compact("FLIT"));
    }

    [Fact]
    public void Imprime_las_fechas_en_hora_de_Colombia_con_el_formato_del_producto()
    {
        var texto = PdfText(new IdentityLetterPdfGenerator().Generate(Data()));

        texto.Should().Contain(Compact("Emitida: 05/11/2026 10:30"));
        texto.Should().Contain(Compact("05/11/2026 10:27")); // fecha de validación
        texto.Should().Contain(Compact("Hasta el 05/12/2026"));
        texto.Should().NotContain(Compact("a. m.")).And.NotContain(Compact("p. m."));
    }

    [Fact]
    public void El_texto_de_autorizacion_es_el_que_la_persona_acepto_en_su_version()
    {
        var texto = PdfText(new IdentityLetterPdfGenerator().Generate(Data()));
        var textos = ManualIdentityAuthorizationTexts.For("manual-ley1581-v2")!;

        texto.Should().Contain(Compact("Autorización versión manual-ley1581-v2 aceptada 05/11/2026 10:10 - Ley 1581 de 2012"));
        texto.Should().Contain(Compact(textos.AuthorizationTitle));
        texto.Should().Contain(Compact("Autorizo a Flit el tratamiento de mis datos personales"));
        texto.Should().Contain(Compact("Autorizo de forma libre, previa y expresa a Flit"));
    }

    [Fact]
    public void Con_una_version_desconocida_no_inventa_el_texto_legal_y_solo_deja_la_version()
    {
        var data = Data() with { ConsentTextVersion = "manual-ley1581-v999", Texts = null };

        var texto = PdfText(new IdentityLetterPdfGenerator().Generate(data));

        texto.Should().Contain(Compact("Autorización versión manual-ley1581-v999"));
        texto.Should().NotContain(Compact("Autorizo de forma libre")).And.NotContain(Compact("Declaro que adelanto"));
    }

    [Fact]
    public void AC2_No_contiene_QR_score_Kyverum_Ley_527_nacimiento_sexo_ni_ubicacion_o_dispositivo()
    {
        var texto = PdfText(new IdentityLetterPdfGenerator().Generate(Data()));

        foreach (var prohibido in new[]
                 {
                     "Kyverum", "Ley 527", "527", "Score", "score", "QR", "Escanee", "nacimiento", "Sexo", "Dispositivo",
                     "Ubicación", "Ubicacion", "Intentos", "Nombre en el documento", "sello de firma", "SELLO DE FIRMA", "Super Admin",
                 })
        {
            texto.Should().NotContain(Compact(prohibido), $"la carta no debe imprimir «{prohibido}»");
        }
    }

    [Fact]
    public void Con_datos_incompletos_genera_el_PDF_con_marcadores_seguros_sin_lanzar()
    {
        var data = Data() with { ConsentIp = null, ConsentAt = null, ConsentTextVersion = null, Texts = null, ValidUntil = null, CompanyName = " " };

        var act = () => new IdentityLetterPdfGenerator().Generate(data);

        act.Should().NotThrow();
    }

    [Fact]
    public void Una_imagen_corrupta_hace_fallar_la_generacion_para_que_se_reintente()
    {
        var data = Data() with { Selfie = [1, 2, 3, 4] };

        var act = () => new IdentityLetterPdfGenerator().Generate(data);

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Deja_un_PDF_de_muestra_para_revision_visual_si_se_pide()
    {
        var dir = Environment.GetEnvironmentVariable("FLIT_LETTER_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            return;

        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "carta-identidad-muestra.pdf"), new IdentityLetterPdfGenerator().Generate(Data()));
    }
}
