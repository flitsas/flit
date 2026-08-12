using System.Text.RegularExpressions;
using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Casilla 19 del FUR ("EMPRESA VINCULADORA" → <c>linked_company_name</c>) medida con la fuente
/// REAL, no con la medición sintética de <see cref="FurTextFitterTests"/>.
///
/// <para><b>Por qué hace falta este archivo aparte.</b> <c>FurTextFitterTests</c> inyecta un
/// <c>measure</c> lineal (ancho = caracteres × cuerpo × factor) para probar el ALGORITMO sin
/// depender de fuentes. Perfecto para eso, e inútil para la pregunta que importa aquí: «¿esta razón
/// social concreta cabe en esta caja concreta?». Esa respuesta depende de las métricas reales, y las
/// reales no son las de Arial del sistema: el resolutor embebido (HU #10855) mapea "Arial" a una
/// TrueType propia que mide ~11% MÁS ANCHO que Helvetica. Estimar con métricas de Helvetica decía
/// que el nombre cabía a 7,10pt; el FUR real salía truncado. De ahí que aquí se mida de verdad.</para>
///
/// <para>Los valores (ancho, alto, cuerpo y piso) se leen DEL MANIFIESTO, no se copian: si alguien
/// vuelve a subir <c>minFontSize</c> o estrecha <c>w</c>, estos tests fallan en vez de quedarse
/// verdes describiendo una calibración que ya no existe.</para>
/// </summary>
public sealed class FurCasilla19FitTests
{
    /// <summary>
    /// El caso real (NIT 890903938). Lo que el RUES devuelve como razón social de Bancolombia: 79
    /// caracteres, con la cláusula de denominación alterna incluida. NO se recorta al imprimir — es
    /// lo que la fuente oficial declara, y la casilla debe mostrarlo íntegro.
    /// </summary>
    private const string RazonSocialBancolombia =
        "BANCOLOMBIA S.A, ADEMÁS PODRÁ GIRAR BAJO LA DENOMINACIÓN BANCO DE COLOMBIA S.A.";

    /// <summary>
    /// Caso corto de referencia de la cuarta tanda: debe seguir cabiendo en UNA línea. Es el
    /// invariante que hizo descartar <c>FitMultiline</c> para este campo (partía en dos líneas lo
    /// que cabía en una), y sigue vigente.
    /// </summary>
    private const string RazonSocialCorta = "TRANSPORTES DEL NORTE S.A.S.";

    /// <summary>
    /// Caso MEDIANO: no cabe en un renglón al cuerpo declarado, pero sí en dos. Es la banda que el
    /// piso de 6,0 puso en riesgo — sin tope al encogido de una línea, el fitter prefería 6,10pt en
    /// UNA línea antes que 7,60pt en dos, en un recuadro con sitio para tres.
    /// </summary>
    private const string RazonSocialMediana = "DISTRIBUIDORA NACIONAL DE CARGA";

    [Fact]
    public void RazonSocialDelRues_SaleCompleta_SinElipsis()
    {
        var (fit, campo) = Encajar(RazonSocialBancolombia);

        var pintado = NormalizarEspacios(string.Join(" ", fit.Lines));

        pintado.Should().Be(
            NormalizarEspacios(RazonSocialBancolombia),
            "la casilla 19 debe mostrar la razón social que devuelve el RUES ÍNTEGRA: es el nombre "
            + "legal de la empresa vinculadora, no un texto decorativo que se pueda recortar");
        fit.Lines.Should().NotContain(l => l.Contains('…'), "truncar aquí es perder el nombre de la empresa");
        fit.FontSize.Should().BeGreaterThanOrEqualTo(
            campo.MinFontSize!.Value, "el encaje nunca puede bajar del piso declarado en el manifiesto");
    }

    /// <summary>
    /// Ninguna línea puede salirse del recuadro: pisar la casilla 20 (justo debajo) o el divisor de
    /// la casilla del NIT (a la derecha, x=703,6) es peor que cualquier problema de cuerpo.
    /// </summary>
    [Fact]
    public void RazonSocialLarga_NoSeSaleDeLaCaja()
    {
        var (fit, campo) = Encajar(RazonSocialBancolombia);

        using var lienzo = NuevoLienzo();
        foreach (var linea in fit.Lines)
            Ancho(lienzo.Gfx, linea, fit.FontSize).Should().BeLessThanOrEqualTo(campo.W);

        var altoOcupado = fit.Lines.Count * fit.FontSize * 1.25;
        altoOcupado.Should().BeLessThanOrEqualTo(campo.H);
    }

    /// <summary>
    /// El caso corto no cambia de aspecto: una sola línea, sin partir. Bajar el piso NO puede
    /// convertir en multilínea lo que siempre cupo en un renglón.
    /// </summary>
    [Fact]
    public void RazonSocialCorta_SigueEnUnaSolaLinea()
    {
        var (fit, _) = Encajar(RazonSocialCorta);

        fit.Lines.Should().ContainSingle().Which.Should().Be(RazonSocialCorta);
    }

    /// <summary>
    /// Bajar el piso NO puede encoger lo que ya salía bien. Un nombre mediano tiene que partirse
    /// conservando (o casi) el cuerpo declarado en vez de comprimirse a un renglón diminuto: la caja
    /// se calibró con alto para varios renglones justamente para eso, y el FUR se imprime y se escanea.
    /// </summary>
    [Fact]
    public void RazonSocialMediana_SeParteEnVezDeEncogerse()
    {
        var (fit, campo) = Encajar(RazonSocialMediana);

        fit.Lines.Count.Should().BeGreaterThan(1, "la caja admite más de un renglón: partir conserva cuerpo");
        fit.FontSize.Should().BeGreaterThanOrEqualTo(
            campo.FontSize * 0.92,
            "encoger más de lo cosmético en una sola línea, con sitio para partir, es la regresión que "
            + "introdujo bajar el piso a 6,0");

        NormalizarEspacios(string.Join(" ", fit.Lines))
            .Should().Be(NormalizarEspacios(RazonSocialMediana));
    }

    /// <summary>
    /// El tope al encogido de una línea no puede costar completitud: el caso del RUES sigue saliendo
    /// entero. Aquí el piso de 6,0 sí hace falta, porque ni partido cabe por encima de él.
    /// </summary>
    [Fact]
    public void RazonSocialLarga_UsaElPisoBajoYSaleEntera()
    {
        var (fit, campo) = Encajar(RazonSocialBancolombia);

        fit.Lines.Count.Should().BeGreaterThan(1);
        fit.FontSize.Should().BeLessThan(
            campo.FontSize * 0.92, "este es el caso que de verdad necesita el piso bajo");
        fit.FontSize.Should().BeGreaterThanOrEqualTo(campo.MinFontSize!.Value);
    }

    // ── Andamiaje ─────────────────────────────────────────────────────────────

    private static (FurTextFit Fit, FurFieldDefinition Campo) Encajar(string texto)
    {
        var campo = Campo();
        using var lienzo = NuevoLienzo();

        var fit = FurTextFitter.Fit(
            texto,
            campo.W,
            campo.H,
            campo.FontSize,
            (valor, cuerpo) => Ancho(lienzo.Gfx, valor, cuerpo),
            campo.MinFontSize);

        return (fit, campo);
    }

    private static FurFieldDefinition Campo() =>
        FurFieldManifestLoader.LoadEmbedded().Fields
            .Single(f => f.Id == "linked_company_name");

    /// <summary>
    /// Misma medición que usa el renderer (<c>FurOverlayRenderer.DrawText</c>): el nombre de familia
    /// "Arial" pasa por el resolutor embebido, así que esto mide la fuente que de verdad se dibuja.
    /// </summary>
    private static double Ancho(XGraphics gfx, string texto, double cuerpo) =>
        gfx.MeasureString(texto, new XFont("Arial", cuerpo, XFontStyle.Bold)).Width;

    private static Lienzo NuevoLienzo()
    {
        FurFontResolver.EnsureRegistered();
        return new Lienzo();
    }

    /// <summary>Página desechable de la que colgar un <see cref="XGraphics"/> con el que medir.</summary>
    private sealed class Lienzo : IDisposable
    {
        private readonly PdfDocument _doc = new();

        public Lienzo() => Gfx = XGraphics.FromPdfPage(_doc.AddPage());

        public XGraphics Gfx { get; }

        public void Dispose()
        {
            Gfx.Dispose();
            _doc.Dispose();
        }
    }

    /// <summary>
    /// El envolvido parte por palabras descartando espacios vacíos, así que un doble espacio del
    /// origen (el RUES los devuelve) desaparece al pintar. Comparar carácter a carácter fallaría por
    /// eso sin que se haya perdido NADA del nombre: lo que se compara es el contenido, no el
    /// espaciado.
    /// </summary>
    private static string NormalizarEspacios(string valor) =>
        Regex.Replace(valor.Trim(), @"\s+", " ");
}
