using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>
/// HU #11643 — ata el presupuesto de caracteres de <see cref="FurObservacionesComposer"/> a la
/// GEOMETRÍA REAL del recuadro, medida con la fuente embebida.
///
/// <para>El presupuesto vive en la capa de aplicación, que no conoce fuentes ni manifiestos: es un
/// número. Sin esta prueba, recalibrar el recuadro —bajarle el alto, subirle el cuerpo— dejaría el
/// número obsoleto en silencio y volveríamos a truncar información obligatoria. Aquí el número se
/// verifica contra el manifiesto en cada ejecución, en los tres formatos.</para>
/// </summary>
public sealed class FurObservacionesPresupuestoTests
{
    private sealed class Lienzo : IDisposable
    {
        private readonly PdfDocument _doc = new();
        public XGraphics Gfx { get; }

        public Lienzo()
        {
            FurFontResolver.EnsureRegistered();
            Gfx = XGraphics.FromPdfPage(_doc.AddPage());
        }

        public void Dispose()
        {
            Gfx.Dispose();
            _doc.Dispose();
        }
    }

    private static FurFieldDefinition? Campo(FurTemplateFormat formato) =>
        FurFieldManifestLoader.LoadEmbedded(formato).Fields.FirstOrDefault(f => f.Id == "observations");

    /// <summary>Texto de relleno con palabras de longitud realista.</summary>
    private static string Relleno(int caracteres) =>
        string.Join(" ", Enumerable.Repeat("VEHICULO VERIFICADO SIN NOVEDAD", (caracteres / 31) + 2))[..caracteres];

    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void ElPresupuestoCabeEnElRecuadro_SinTruncar(FurTemplateFormat formato)
    {
        var campo = Campo(formato);
        if (campo is null)
        {
            Assert.Skip($"el formato {formato} no declara recuadro de observaciones");
            return;
        }

        using var lienzo = new Lienzo();
        var texto = Relleno(FurObservacionesComposer.PresupuestoCaracteres);

        var fit = FurTextFitter.FitMultiline(
            texto, campo.W, campo.H, campo.FontSize,
            (valor, cuerpo) => lienzo.Gfx.MeasureString(
                valor, new XFont("Arial", cuerpo, campo.Bold ? XFontStyle.Bold : XFontStyle.Regular)).Width,
            null,
            campo.MinFontSize);

        string.Join(" ", fit.Lines).Should().NotContain("…",
            "un texto del tamaño del presupuesto debe entrar ENTERO en el recuadro del formato {0} " +
            "(w={1}, h={2}, cuerpo={3}). Si esto falla, o se recalibró el recuadro o el presupuesto de " +
            "FurObservacionesComposer se quedó grande: ajusta el presupuesto, no esta prueba.",
            formato, campo.W, campo.H, campo.FontSize);

        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(campo.H,
            "el texto del presupuesto debe caber en el ALTO declarado, sin tinta fuera del recuadro");
    }

    /// <summary>
    /// El presupuesto no puede ser tan holgado que obligue al auto-encaje a bajar por debajo del
    /// mínimo legible: caber no basta si el resultado no se puede leer en el formulario impreso.
    /// </summary>
    [Fact]
    public void ElPresupuestoNoObligaACuerpoIlegible()
    {
        var campo = Campo(FurTemplateFormat.Automotor)!;
        using var lienzo = new Lienzo();

        var fit = FurTextFitter.FitMultiline(
            Relleno(FurObservacionesComposer.PresupuestoCaracteres), campo.W, campo.H, campo.FontSize,
            (valor, cuerpo) => lienzo.Gfx.MeasureString(
                valor, new XFont("Arial", cuerpo, campo.Bold ? XFontStyle.Bold : XFontStyle.Regular)).Width,
            null,
            campo.MinFontSize);

        fit.FontSize.Should().BeGreaterThanOrEqualTo(5,
            "5 pt es el piso de legibilidad que asume FurTextFitter para el recuadro");
    }
}
