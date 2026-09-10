using Flit.Infrastructure.Documents.Fur;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #11048 — encaje del texto en la caja declarada del campo del FUR. Se usa una medición lineal
/// inyectada (ancho = caracteres × cuerpo × factor) para que el algoritmo se pruebe sin PdfSharpCore ni
/// fuentes del sistema. Geometría real del caso reportado: el campo de nombre del propietario declara
/// <c>w = 93.5</c>, <c>h = 14.4</c> y cuerpo <c>7.7</c>.
/// </summary>
public sealed class FurTextFitterTests
{
    /// <summary>Ancho aproximado de carácter: 0,5 × cuerpo (parecido a una sans a ojo).</summary>
    private static double Measure(string text, double fontSize) => text.Length * fontSize * 0.5;

    private const double CampoW = 93.5;
    private const double CampoH = 14.4;
    private const double CampoFont = 7.7;

    private static FurTextFit Fit(string text, double w = CampoW, double h = CampoH, double font = CampoFont) =>
        FurTextFitter.Fit(text, w, h, font, Measure);

    [Fact]
    public void TextoQueYaCabe_NoSeToca()
    {
        // 12 chars × 7.7 × 0.5 = 46.2 pt < 93.5 ⇒ intacto, con el cuerpo calibrado del manifiesto.
        var fit = Fit("Juan Pérez A");

        fit.Lines.Should().Equal("Juan Pérez A");
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void TextoAlgoMasLargo_ReduceElCuerpoYSigueEnUnaLinea()
    {
        // 28 chars: no cabe a 7.7 (107.8) pero sí a 6.5 (91) ⇒ una línea con cuerpo menor.
        var fit = Fit(new string('A', 28));

        fit.Lines.Should().HaveCount(1);
        fit.FontSize.Should().BeLessThan(CampoFont).And.BeGreaterThanOrEqualTo(CampoFont * 0.65);
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(CampoW);
    }

    [Fact]
    public void RazonSocialLarga_SeParteEnVariasLineasDentroDelAlto()
    {
        var fit = Fit("COMERCIALIZADORA INTERNACIONAL DE VEHICULOS Y MAQUINARIA S.A.S");

        fit.Lines.Count.Should().BeGreaterThan(1);
        // Todas las líneas caben en el ancho…
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        // …y el bloque cabe en el alto del campo.
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
    }

    [Fact]
    public void NuncaBajaDelMinimoLegible()
    {
        var fit = Fit(new string('X', 400));

        fit.FontSize.Should().BeGreaterThanOrEqualTo(Math.Max(3, CampoFont * 0.65));
    }

    [Fact]
    public void CuandoNadaCabe_TruncaConElipsisEnUnaSolaLinea()
    {
        // Una única palabra larguísima no se puede partir por palabras: se trunca.
        var fit = Fit(new string('X', 400));

        fit.Lines.Should().HaveCount(1);
        fit.Lines[0].Should().EndWith("…");
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(CampoW);
    }

    [Fact]
    public void CampoSinAnchoDeclarado_NoAlteraElTexto()
    {
        var fit = Fit("CUALQUIER COSA MUY LARGA QUE NO SE MIDE", w: 0);

        fit.Lines.Should().Equal("CUALQUIER COSA MUY LARGA QUE NO SE MIDE");
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void CampoDeUnaSolaLinea_NoParteAunqueElTextoSeaLargo()
    {
        // Alto justo para una línea a cualquier cuerpo admisible ⇒ no hay wrap posible.
        var fit = Fit("EMPRESA DE TRANSPORTES Y LOGISTICA NACIONAL", h: 6);

        fit.Lines.Should().HaveCount(1);
    }

    // El caso concreto que reportó el negocio.
    [Fact]
    public void BancolombiaSas_CabeDentroDelCampo()
    {
        var fit = Fit("BANCOLOMBIA S.A.S");

        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
    }

    [Fact]
    public void NombreDeOrganismoTipico_CabeEnUnaLineaConElAnchoDelRecuadro()
    {
        // Sección 1: el recuadro de maquinaria/remolques deja ~230 pt hasta la placa. Con el ancho
        // viejo (114) el fitter partía nombres de secretaría y la segunda línea se salía de la celda.
        const double organismoW = 230;
        const double organismoH = 22;
        const double organismoFont = 5.5;
        var fit = FurTextFitter.Fit(
            "INSTITUTO DE TRANSITO Y TRANSPORTE DE SANTANDER",
            organismoW,
            organismoH,
            organismoFont,
            Measure);

        fit.Lines.Should().HaveCount(1);
        fit.FontSize.Should().Be(organismoFont);
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(organismoW);
    }

    [Fact]
    public void NombreDeOrganismoMuyLargo_ParteDentroDelAltoDelRecuadro()
    {
        const double organismoW = 230;
        const double organismoH = 22;
        const double organismoFont = 5.5;
        var fit = FurTextFitter.Fit(
            "SECRETARIA DE MOVILIDAD Y TRANSPORTE DEL DISTRITO ESPECIAL INDUSTRIAL Y PORTUARIO DE BARRANQUILLA",
            organismoW,
            organismoH,
            organismoFont,
            Measure);

        fit.Lines.Count.Should().BeGreaterThan(1);
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= organismoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(organismoH);
    }

    // HU sin ADO 2026-08-11 (tercera tanda) — piso ABSOLUTO opcional (Plan B de la casilla 19
    // "EMPRESA VINCULADORA" del FUR): un texto que necesitaría bajar del piso por defecto
    // (65% × cuerpo) para caber se trunca en su lugar, en vez de seguir encogiendo hasta casi
    // ilegible. Un texto que cabe ANTES de tocar el piso no se ve afectado (mismo resultado con o
    // sin `minFontSize`).
    [Fact]
    public void MinFontSize_ExplicitoReemplazaElPisoPorDefecto()
    {
        // Con el piso por defecto (65% × 7.7 ≈ 5.0) este texto encajaría en una línea más pequeña.
        // Con minFontSize=7.0 (más alto que 5.0) debe truncar en vez de encoger por debajo de 7.0.
        var sinPiso = FurTextFitter.Fit(new string('A', 40), CampoW, CampoH, CampoFont, Measure);
        var conPisoAlto = FurTextFitter.Fit(new string('A', 40), CampoW, CampoH, CampoFont, Measure, minFontSize: 7.0);

        sinPiso.FontSize.Should().BeLessThan(7.0); // encogió por debajo de 7.0 sin el piso explícito
        conPisoAlto.FontSize.Should().Be(7.0);
        conPisoAlto.Lines[0].Should().EndWith("…", "no cabe ni al piso de 7.0, así que trunca");
    }

    [Fact]
    public void MinFontSize_NuncaBajaDelPisoExplicito()
    {
        var fit = FurTextFitter.Fit(new string('X', 400), CampoW, CampoH, CampoFont, Measure, minFontSize: 6.5);

        fit.FontSize.Should().Be(6.5);
    }

    [Fact]
    public void MinFontSize_Null_ConservaElComportamientoDeSiempre()
    {
        // Pasar null explícito es idéntico a no pasar el parámetro (default).
        var conNull = FurTextFitter.Fit(new string('A', 28), CampoW, CampoH, CampoFont, Measure, minFontSize: null);
        var sinParametro = Fit(new string('A', 28));

        conNull.FontSize.Should().Be(sinParametro.FontSize);
        conNull.Lines.Should().Equal(sinParametro.Lines);
    }

    [Fact]
    public void MinFontSize_TextoQueYaCabeIgnoraElPiso()
    {
        // El piso solo importa cuando hace falta encoger; un texto que ya cabe al cuerpo declarado
        // no lo consulta en absoluto (paso 1, passthrough).
        var fit = FurTextFitter.Fit("Juan Pérez A", CampoW, CampoH, CampoFont, Measure, minFontSize: 7.0);

        fit.Lines.Should().Equal("Juan Pérez A");
        fit.FontSize.Should().Be(CampoFont);
    }

    // HU sin ADO 2026-08-11 (cuarta tanda) — el piso puede no coincidir con la rejilla de pasos de
    // 0,25 desde `baseFontSize`: sin una prueba explícita AL piso exacto, un texto que sí cabría justo
    // ahí caía al paso siguiente (partir en líneas, o truncar) sin necesidad. Caso real: base 7.6,
    // piso 7.0 — el bucle prueba 7.35 y 7.10 y se detiene en 6.85 (por debajo del piso), sin tocar
    // nunca el 7.0 exacto.
    [Fact]
    public void MinFontSize_SeProbaExplicitamenteAunqueNoCaigaEnLaRejillaDePasos()
    {
        // Con Measure lineal (ancho = chars × size × 0.5) y base=7.6/piso=7.0: el bucle de reducción
        // prueba 7.35 y 7.10 (7.6 menos pasos de 0.25) y se detiene ahí — 6.85 ya está por debajo del
        // piso. Un texto de 26 caracteres mide 95.55 a 7.35 y 92.3 a 7.10 (ninguno cabe en w=91), pero
        // EXACTAMENTE 91.0 al piso de 7.0 (26 × 7.0 × 0.5) — ese tamaño nunca lo toca la rejilla de
        // pasos, solo la prueba explícita al piso.
        var texto = new string('A', 26);
        var fit = FurTextFitter.Fit(texto, maxWidth: 91, maxHeight: CampoH, baseFontSize: 7.6, Measure, minFontSize: 7.0);

        // Solo el piso EXACTO (7.0) hace que el texto quepa en una línea.
        fit.Lines.Should().Equal(texto);
        fit.FontSize.Should().Be(7.0);
    }

    // HU sin ADO 2026-08-11 (cuarta tanda) — casilla 19 del FUR: cuando ni una línea ni el ancho
    // alcanzan ni siquiera al piso, el último recurso ahora aprovecha TODAS las líneas que el alto
    // admite antes de truncar (como ya hacía `FitMultiline`), en vez de colapsar todo a una sola línea
    // truncada y desperdiciar el resto del campo.
    [Fact]
    public void UltimoRecurso_ConAltoParaVariasLineas_AprovechaTodasAntesDeTruncar()
    {
        // Texto con espacios (partible) que no cabe ni en 3 líneas al piso: se esperan 3 líneas, la
        // última con elipsis, ninguna más ancha que el campo.
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 30));
        // h=27 con Measure lineal (0.5×size×1.25 líneas): a piso 7.0, lineHeight=8.75 ⇒ MaxLines=3.
        var fit = FurTextFitter.Fit(texto, maxWidth: 128, maxHeight: 27, baseFontSize: 7.6, Measure, minFontSize: 7.0);

        fit.FontSize.Should().Be(7.0);
        fit.Lines.Should().HaveCount(3);
        fit.Lines[^1].Should().EndWith("…");
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= 128);
    }

    [Fact]
    public void UltimoRecurso_ConAltoDeUnaLinea_SigueColapsandoAUnaSolaLineaTruncada()
    {
        // h pequeño (como los campos de nombre de siempre, h≈14): MaxLines(14, 7.0)=1 ⇒ el
        // comportamiento de última instancia sigue siendo el de HU #11048, sin regresión.
        var fit = FurTextFitter.Fit(new string('X', 400), CampoW, CampoH, CampoFont, Measure, minFontSize: 6.5);

        fit.Lines.Should().HaveCount(1);
        fit.Lines[0].Should().EndWith("…");
    }

    // HU sin ADO 2026-08-11 (cuarta tanda) — casilla 19: con `h` suficiente para varias líneas, un
    // texto que no cabe en una sola línea se PARTE en líneas al cuerpo base o al piso, nunca se
    // queda en una sola línea truncada mientras haya alto disponible sin usar.
    [Fact]
    public void ConAltoParaVariasLineas_PrefiereEnvolverAntesQueTruncarEnUnaSolaLinea()
    {
        var texto = "TRANSPORTE ESPECIAL Y MASIVO DE PASAJEROS DEL EJE CAFETERO Y ALREDEDORES";
        var fit = FurTextFitter.Fit(texto, maxWidth: 128, maxHeight: 27, baseFontSize: 7.6, Measure, minFontSize: 7.0);

        fit.Lines.Count.Should().BeGreaterThan(1);
    }
}

/// <summary>
/// HU #11256 — encaje del texto en la caja declarada de un campo <c>multiline</c> con
/// <c>autoFit: true</c> (hoy, solo <c>observations</c>). Geometría del caso real (automotor):
/// caja 403.1 × 33.0 pt, cuerpo base 7.2. La medición se inyecta igual que en <see cref="FurTextFitterTests"/>
/// para que el algoritmo se pruebe sin PdfSharpCore ni fuentes del sistema.
/// </summary>
public sealed class FurTextFitterFitMultilineTests
{
    /// <summary>Ancho aproximado de carácter: 0,5 × cuerpo (igual criterio que <see cref="FurTextFitterTests"/>).</summary>
    private static double Measure(string text, double fontSize) => text.Length * fontSize * 0.5;

    private const double CampoW = 403.1;
    private const double CampoH = 33.0;
    private const double CampoFont = 7.2;

    private static FurTextFit FitMultiline(
        string text, double w = CampoW, double h = CampoH, double font = CampoFont, Action<int>? onTruncate = null) =>
        FurTextFitter.FitMultiline(text, w, h, font, Measure, onTruncate);

    [Fact]
    public void TextoQueYaCabe_PassthroughExacto_MismasLineasYMismoCuerpo()
    {
        // 30 chars × 7.2 × 0.5 = 108 pt < 403.1 ⇒ una sola línea, cuerpo intacto (garantía CF4).
        const string texto = "Vehículo con platón adaptado.";
        var fit = FitMultiline(texto);

        fit.Lines.Should().Equal(texto);
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void TextoVacio_PassthroughDevuelveSinLineas()
    {
        var fit = FitMultiline(string.Empty);

        fit.Lines.Should().BeEmpty();
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void AnchoCero_NoAlteraElTexto()
    {
        var fit = FitMultiline("CUALQUIER OBSERVACIÓN MUY LARGA QUE NO SE MIDE PORQUE EL CAMPO NO DECLARA ANCHO", w: 0);

        fit.Lines.Should().Equal(["CUALQUIER OBSERVACIÓN MUY LARGA QUE NO SE MIDE PORQUE EL CAMPO NO DECLARA ANCHO"]);
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void SaltosDeLineaExplicitos_SeRespetanComoParrafosSeparados()
    {
        // Dos párrafos cortos que caben cada uno en el ancho y cuyo bloque cabe en el alto: passthrough,
        // preservando el salto duro como dos líneas (no se concatenan en una).
        var fit = FitMultiline("Primera línea corta.\nSegunda línea corta.");

        fit.Lines.Should().Equal("Primera línea corta.", "Segunda línea corta.");
        fit.FontSize.Should().Be(CampoFont);
    }

    [Fact]
    public void ParrafoLargo_SeEnvuelveAlCuerpoBase_SinReducirElCuerpo()
    {
        // Un párrafo que no cabe en una línea al cuerpo base pero cuyo envolvido sí cabe en el alto de 3
        // líneas (CampoH=33 ⇒ MaxLines(7.2)=floor(33/9)=3).
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 20)); // 20×"PALABRA " ≈ 160 chars

        var fit = FitMultiline(texto);

        fit.FontSize.Should().Be(CampoFont);
        fit.Lines.Count.Should().BeGreaterThan(1).And.BeLessThanOrEqualTo(3);
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
    }

    [Fact]
    public void ParrafoMuyLargo_ReduceElCuerpoReenvolviendo()
    {
        // Demasiadas palabras para caber en 3 líneas al cuerpo base: baja de cuerpo re-envolviendo hasta
        // que el número de líneas quepa en el alto.
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 60));

        var fit = FitMultiline(texto);

        fit.FontSize.Should().BeLessThan(CampoFont).And.BeGreaterThanOrEqualTo(FurTextFitterFitMultilineTests.MinMultilineFontSizeForTests);
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
    }

    /// <summary>Espejo de <c>FurTextFitter.MinMultilineFontSize</c> (privado): el piso documentado en el diseño.</summary>
    private const double MinMultilineFontSizeForTests = 5;

    [Fact]
    public void TextoDesmedido_NuncaBajaDelPisoDe5Puntos()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400)); // ≈2.800 caracteres

        var fit = FitMultiline(texto);

        fit.FontSize.Should().Be(MinMultilineFontSizeForTests);
    }

    [Fact]
    public void TextoDesmedido_TruncaConElipsisYAvisaCaracteresElididos()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400));
        int? elidedChars = null;

        var fit = FitMultiline(texto, onTruncate: n => elidedChars = n);

        fit.Lines[^1].Should().EndWith("…");
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        (fit.Lines.Count * fit.FontSize * 1.25).Should().BeLessThanOrEqualTo(CampoH);
        elidedChars.Should().NotBeNull().And.BeGreaterThan(0);
    }

    [Fact]
    public void PalabraUnicaMasAnchaQueLaCaja_NuncaSeDibujaFueraDeLaCaja()
    {
        // Una sola "palabra" (sin espacios) tan larga que ningún cuerpo entre el base y el piso la hace
        // caber: debe terminar truncada, nunca desbordando el ancho declarado.
        var fit = FitMultiline(new string('X', 400));

        fit.Lines.Should().HaveCount(1);
        fit.Lines[0].Should().EndWith("…");
        Measure(fit.Lines[0], fit.FontSize).Should().BeLessThanOrEqualTo(CampoW);
    }

    [Fact]
    public void CabeConVariasLineasCortas_SinLlegarAlLimiteDeAlto_QuedaAlCuerpoBase()
    {
        // Tres párrafos con `\n` explícitos, cada uno corto: caben tal cual al cuerpo base porque tanto
        // el ancho por párrafo como el alto total (3 líneas) respetan la caja.
        var fit = FitMultiline("Línea uno.\nLínea dos.\nLínea tres.");

        fit.Lines.Should().Equal("Línea uno.", "Línea dos.", "Línea tres.");
        fit.FontSize.Should().Be(CampoFont);
    }

    // ── Guarda de entrada y coste del truncado (hallazgo de seguridad del PR #230) ──────────────

    [Fact]
    public void TokenDesmedidoSinEspacios_TerminaEnTiempoRazonable_YNoDesbordaElAncho()
    {
        // El descenso carácter a carácter medía la cadena entera en cada vuelta: sobre un token sin
        // espacios eso es O(N²) y con un pegado largo dejaba la generación del FUR colgada. Con la
        // búsqueda binaria el número de mediciones es logarítmico en la longitud del corte.
        var mediciones = 0;
        double Contando(string t, double f) { mediciones++; return Measure(t, f); }

        var fit = FurTextFitter.FitMultiline(
            new string('X', 100_000), CampoW, CampoH, CampoFont, Contando);

        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        fit.Lines[^1].Should().EndWith("…");
        // Cota generosa: lo que importa es que no crezca con N. El bucle lineal habría hecho decenas
        // de miles de mediciones solo en el truncado final.
        mediciones.Should().BeLessThan(2_000);
    }

    [Fact]
    public void EntradaPorEncimaDelTope_SeRecorta_YLoElididoSeReportaCompleto()
    {
        // La guarda recorta antes de medir, pero lo recortado tiene que sumarse a lo que se informa:
        // perder texto de un documento oficial en silencio es justo lo que no se quiere.
        var elidedChars = 0;
        var fit = FitMultiline(new string('X', 50_000), onTruncate: n => elidedChars = n);

        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
        // Lo reportado incluye tanto lo que quitó la guarda como lo que elidió el último recurso.
        elidedChars.Should().BeGreaterThan(42_000);
    }

    // HU sin ADO 2026-08-11 (cuarta tanda) — el coordinador pidió verificar explícitamente si
    // `FitMultiline` honraba un piso propio: NO lo hacía (usaba `MinMultilineFontSize = 5` fijo en
    // todos sus pasos). Se extendió con el mismo parámetro opcional `minFontSize` que ya tenía `Fit`
    // — `observations` sigue sin pasarlo, así que su piso de 5pt no cambia (tests de arriba).
    [Fact]
    public void MinFontSize_ExplicitoReemplazaElPisoPorDefectoDe5()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400)); // el mismo texto desmedido de arriba

        var sinPiso = FitMultiline(texto); // piso por defecto: 5
        var conPisoAlto = FurTextFitter.FitMultiline(texto, CampoW, CampoH, CampoFont, Measure, minFontSize: 7.0);

        sinPiso.FontSize.Should().Be(MinMultilineFontSizeForTests);
        conPisoAlto.FontSize.Should().Be(7.0);
        conPisoAlto.FontSize.Should().BeGreaterThan(sinPiso.FontSize, "el piso explícito es más alto que el de siempre");
    }

    [Fact]
    public void MinFontSize_NuncaBajaDelPisoExplicitoEnFitMultiline()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400));

        var fit = FurTextFitter.FitMultiline(texto, CampoW, CampoH, CampoFont, Measure, minFontSize: 6.8);

        fit.FontSize.Should().Be(6.8);
        fit.Lines.Should().OnlyContain(l => Measure(l, fit.FontSize) <= CampoW);
    }

    [Fact]
    public void MinFontSize_NullEnFitMultiline_ConservaElPisoDeSiempre()
    {
        var texto = string.Join(" ", Enumerable.Repeat("PALABRA", 400));

        var conNull = FurTextFitter.FitMultiline(texto, CampoW, CampoH, CampoFont, Measure, minFontSize: null);
        var sinParametro = FitMultiline(texto);

        conNull.FontSize.Should().Be(sinParametro.FontSize).And.Be(MinMultilineFontSizeForTests);
    }

    [Fact]
    public void BusquedaBinaria_DevuelveElMismoPrefijoQueElDescensoLineal()
    {
        // Blindaje del refactor: el prefijo elegido debe ser EXACTAMENTE el mayor que cabe. Se contrasta
        // contra el descenso lineal, que era la implementación anterior.
        var fit = FitMultiline(new string('Y', 300));
        var pintada = fit.Lines[^1];

        var esperado = new string('Y', 300);
        while (esperado.Length > 0 && Measure(esperado + "…", fit.FontSize) > CampoW)
            esperado = esperado[..^1];

        pintada.Should().Be(esperado + "…");
    }
}
