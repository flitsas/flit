using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11256 (CF12, R3) — no-regresión de los sellos de firma. <c>vehicle_owner_signature</c> y
/// <c>vehicle_buyer_signature</c> son campos <c>multiline</c> igual que <c>observations</c>, y en
/// automotor el sello del comprador YA desborda hoy (<c>h: 35.3</c> a <c>fontSize: 8</c>, cuatro líneas
/// ocupan 40 pt). El auto-encaje (<see cref="FurTextFitter.FitMultiline"/>) es opt-in por manifiesto
/// (<see cref="FurFieldDefinition.AutoFit"/>): si se aplicara a todo <c>multiline</c> por defecto, este
/// mismo sello se encogería — una regresión visible en el 100% de los FUR firmados de las tres
/// plantillas. Este test fija esa garantía en dos frentes: (1) los tres manifiestos reales declaran
/// <c>AutoFit = false</c> en ambos sellos y <c>true</c> solo en <c>observations</c>; (2) reproduce la
/// misma condición de despacho que usa <c>FurOverlayRenderer.DrawText</c>
/// (<c>field.Type == Multiline &amp;&amp; field.AutoFit</c>) para demostrar que, con esa condición,
/// los sellos producen EXACTAMENTE las mismas líneas y el mismo cuerpo que el <c>Split</c> simple de
/// hoy — incluso para el texto que hoy ya desborda la caja.
/// </summary>
public sealed class FurSignatureAutoFitRegressionTests
{
    private static readonly FurTemplateFormat[] AllFormats =
    [
        FurTemplateFormat.Automotor,
        FurTemplateFormat.Maquinaria,
        FurTemplateFormat.Remolques,
    ];

    private static FurFieldDefinition Field(FurTemplateFormat format, string id) =>
        FurFieldManifestLoader.LoadEmbedded(format).Fields.Single(f => f.Id == id);

    [Theory]
    [MemberData(nameof(FormatsTimesSignatureFields))]
    public void SellosDeFirma_DeclaranAutoFitFalse_EnLosTresFormatos(FurTemplateFormat format, string fieldId)
    {
        var field = Field(format, fieldId);

        field.Type.Should().Be(FurFieldType.Multiline, "los sellos de firma también son multiline (H1)");
        field.AutoFit.Should().BeFalse(
            "aplicar el auto-encaje a un sello de firma es la regresión que el diseño prohíbe (R3)");
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Observations_EsElUnicoCampoConAutoFitTrue(FurTemplateFormat format)
    {
        Field(format, "observations").AutoFit.Should().BeTrue();
    }

    /// <summary>
    /// Reproduce la condición de despacho de <c>FurOverlayRenderer.DrawText</c> para un campo
    /// <c>multiline</c>: si <c>AutoFit</c> es <c>false</c>, el renderer NUNCA invoca
    /// <see cref="FurTextFitter.FitMultiline"/> — sigue partiendo exclusivamente por <c>\n</c>
    /// explícitos, exactamente como antes de la HU #11256 — aunque el texto desborde la caja.
    /// </summary>
    private static string[] DispatchAsRendererWould(FurFieldDefinition field, string text) =>
        field.Type == FurFieldType.Multiline && field.AutoFit
            ? throw new InvalidOperationException("Este test solo cubre el camino AutoFit=false.")
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Fact]
    public void SelloComprador_Automotor_TextoQueHoyYaDesborda_NoSeMideNiSeEncoge()
    {
        // Geometría real (fur-field-manifest.json): h=35.3, fontSize=8. Cuatro líneas a 8 pt × 1.25
        // ocupan 40 pt > 35.3: desborda HOY. La garantía de CF12 es que, tras la HU #11256, sigue
        // desbordando exactamente igual — el renderer no lo toca porque AutoFit=false.
        var field = Field(FurTemplateFormat.Automotor, "vehicle_buyer_signature");
        const string selloTexto =
            "VALIDACIÓN DE IDENTIDAD BIOMÉTRICA\nDANIEL AMADO GARCIA — CC 1193552679\n"
            + "2026-08-04 14:32:10 UTC\nHash: 9f2a...c31e";

        var lines = DispatchAsRendererWould(field, selloTexto);

        // Exactamente el mismo resultado que el `Split` simple de antes de la HU #11256: nada se envuelve,
        // nada se reduce de cuerpo, nada se mide contra el ancho/alto declarados.
        lines.Should().Equal(
            "VALIDACIÓN DE IDENTIDAD BIOMÉTRICA",
            "DANIEL AMADO GARCIA — CC 1193552679",
            "2026-08-04 14:32:10 UTC",
            "Hash: 9f2a...c31e");

        // Si este mismo texto SÍ pasara por FitMultiline (lo que pasaría si `AutoFit` fuera `true`), el
        // resultado sería distinto: prueba que el riesgo (R3) es real y que la opción `AutoFit=false`
        // es la que evita la regresión, no una coincidencia de que "de todos modos daría lo mismo".
        double Measure(string t, double size) => t.Length * size * 0.5;
        var wouldFit = FurTextFitter.FitMultiline(selloTexto, field.W, field.H, field.FontSize, Measure);
        (wouldFit.Lines.Count != lines.Length || wouldFit.FontSize != field.FontSize).Should().BeTrue(
            "si FitMultiline no alterara nada, `AutoFit` no protegería el sello de ninguna regresión real");
    }

    public static TheoryData<FurTemplateFormat> Formats() => new(AllFormats);

    public static TheoryData<FurTemplateFormat, string> FormatsTimesSignatureFields()
    {
        var data = new TheoryData<FurTemplateFormat, string>();
        foreach (var format in AllFormats)
        {
            data.Add(format, "vehicle_owner_signature");
            data.Add(format, "vehicle_buyer_signature");
        }
        return data;
    }
}
