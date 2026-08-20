using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>
/// HU #11640 (AC6) — verifica la geometría de las casillas contra el FORMULARIO OFICIAL, leyendo los
/// trazos impresos del blank en cada ejecución.
///
/// <para><b>El hueco que cierra.</b> <c>FurManifestGuardTests</c> compara el manifiesto contra una
/// línea base congelada y <c>FurPrendaMarkingTests</c> compara strings del diccionario del mapper.
/// Ninguna de las dos sabe DÓNDE cae la tinta. Por eso el descuadre de las casillas de prenda
/// —introducido por una recalibración automática por anclas el 2026-07-24 y propagado el 2026-08-05—
/// convivió con 128 pruebas en verde: la constitución de prenda se estampaba sobre «17 CAMBIO DE
/// CARROCERÍA» y el levantamiento sobre «18 OTROS», y al regenerar la línea base el error quedó
/// congelado como esperado.</para>
///
/// <para><b>Por qué estas aserciones y no coordenadas.</b> Fijar números sería crear otra línea base,
/// con el mismo defecto: se "arregla" regenerándola. Lo que se afirma aquí son RELACIONES que el
/// formulario oficial impone y que ninguna recalibración puede cambiar sin romper el documento —que
/// cada marca caiga dentro de una celda impresa, y que las casillas de prenda ocupen la fila
/// inmediatamente siguiente a las de matrícula/traspaso—. Las líneas salen del content stream del
/// propio blank, así que la referencia es el documento, no nosotros.</para>
/// </summary>
public sealed class FurGeometriaCasillasTests
{
    /// <summary>
    /// La caja declarada en el manifiesto (X,Y + Size) es un PROXY del trazo real: el renderer dibuja
    /// una "X" con línea base en <c>Y + Size*0,85</c> y ancho de glifo menor que <c>Size</c>, así que la
    /// tinta siempre cae dentro de la caja. Medido sobre los tres formatos, el mayor solape de la caja
    /// con un borde impreso es de 0,6 pt y corresponde a casillas dibujadas sobre recuadros muy
    /// pequeños (blindaje, tipos de documento), donde la X es deliberadamente mayor que el recuadrito.
    /// Un punto de tolerancia distingue ese roce cosmético de lo que sí importa: una marca que aterriza
    /// en OTRA celda, que es lo que ocurrió con las casillas de prenda (20 pt de desvío, una fila).
    /// </summary>
    private const double ToleranciaProxy = 1.0;

    private static (List<Segmento> Horizontales, List<Segmento> Verticales) Trazos(FurTemplateFormat formato) =>
        FurPlantillaGeometria.Trazos(FurTemplatePaths.FileNamesFor(formato).P1);

    private static FurFieldDefinition Casilla(FurTemplateFormat formato, string id) =>
        FurFieldManifestLoader.LoadEmbedded(formato).Fields.Single(f => f.Id == id);

    /// <summary>Celda impresa que encierra la casilla; falla el test si no hay ninguna.</summary>
    private static Celda CeldaDe(FurTemplateFormat formato, string id)
    {
        var f = Casilla(formato, id);
        var celda = FurPlantillaGeometria.CeldaQueEncierra(
            Trazos(formato), f.X, f.Y, f.X + f.Size, f.Y + f.Size);

        celda.Should().NotBeNull(
            "la casilla '{0}' del formato {1} debe caer dentro de una celda impresa del formulario", id, formato);
        return celda!.Value;
    }

    // ── Lo que rompió: la fila de las casillas de prenda ──────────────────────

    /// <summary>
    /// En los tres formatos, «inscripción de prenda» y «levantamiento de prenda» están en la fila
    /// INMEDIATAMENTE SIGUIENTE a la de matrícula/traspaso. Con el descuadre, la fila de prenda caía
    /// una fila más abajo (la de cambio de carrocería / otros) y esta aserción falla.
    /// </summary>
    [Fact]
    public void Prenda_OcupaLaFilaSiguienteAMatricula()
    {
        const FurTemplateFormat formato = FurTemplateFormat.Automotor;
        var matricula = CeldaDe(formato, "requested_process_1");
        var constitucion = CeldaDe(formato, "requested_process_11");
        var levantamiento = CeldaDe(formato, "requested_process_12");

        constitucion.Y0.Should().BeApproximately(matricula.Y1, 1.0,
            "la casilla de inscripción de prenda ({0}) debe empezar donde termina la fila de " +
            "matrícula ({1}) en el formato {2}: si queda más abajo, la marca cae en otra fila del " +
            "formulario", constitucion, matricula, formato);

        levantamiento.Y0.Should().BeApproximately(matricula.Y1, 1.0,
            "la casilla de levantamiento de prenda ({0}) debe empezar donde termina la fila de " +
            "matrícula ({1}) en el formato {2}", levantamiento, matricula, formato);
    }

    /// <summary>Constitución y levantamiento comparten fila y ocupan columnas distintas y contiguas.</summary>
    [Fact]
    public void Prenda_ConstitucionYLevantamiento_MismaFilaColumnasDistintas()
    {
        const FurTemplateFormat formato = FurTemplateFormat.Automotor;
        var constitucion = CeldaDe(formato, "requested_process_11");
        var levantamiento = CeldaDe(formato, "requested_process_12");

        constitucion.Y0.Should().BeApproximately(levantamiento.Y0, 1.0,
            "ambas modalidades de prenda están en la misma fila del formulario ({0})", formato);
        constitucion.Y1.Should().BeApproximately(levantamiento.Y1, 1.0,
            "ambas modalidades de prenda están en la misma fila del formulario ({0})", formato);

        constitucion.X0.Should().NotBe(levantamiento.X0,
            "constitución y levantamiento son celdas distintas: si comparten columna, una de las dos " +
            "está pintando sobre la otra ({0})", formato);
    }

    /// <summary>Matrícula y traspaso comparten fila: es la referencia contra la que se sitúa prenda.</summary>
    [Fact]
    public void MatriculaYTraspaso_CompartenFila()
    {
        const FurTemplateFormat formato = FurTemplateFormat.Automotor;
        var matricula = CeldaDe(formato, "requested_process_1");
        var traspaso = CeldaDe(formato, "requested_process_2");

        traspaso.Y0.Should().BeApproximately(matricula.Y0, 1.0, "formato {0}", formato);
        traspaso.X0.Should().NotBe(matricula.X0,
            "matrícula y traspaso son celdas distintas ({0})", formato);
    }

    /// <summary>
    /// Los ocho tipos de combustible ocupan una única fila y ocho celdas DISTINTAS.
    ///
    /// <para>Se comprobó durante el análisis de la HU que estas casillas NO estaban descuadradas, pese
    /// a haber pasado por el mismo barrido automático que las de prenda: caen las ocho en su columna.
    /// Que la X se solape con el dígito impreso es propio del formulario —el número va centrado en la
    /// celda y no hay recuadro de marcado aparte—, no un defecto. Este test fija ese hecho para que no
    /// se vuelva a "corregir" algo que está bien.</para>
    /// </summary>
    [Fact]
    public void Combustible_OchoCeldasDistintasEnLaMismaFila()
    {
        var trazos = Trazos(FurTemplateFormat.Automotor);
        var celdas = FurFieldManifestLoader.LoadEmbedded(FurTemplateFormat.Automotor).Fields
            .Where(f => f.Id.StartsWith("vehicle_fuel_type_", StringComparison.Ordinal))
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => FurPlantillaGeometria.CeldaQueEncierra(trazos, f.X, f.Y, f.X + f.Size, f.Y + f.Size))
            .ToList();

        celdas.Should().HaveCount(8).And.NotContainNulls("las ocho casillas de combustible están recuadradas");

        var valores = celdas.Select(c => c!.Value).ToList();
        valores.Select(c => (Math.Round(c.X0, 1), Math.Round(c.X1, 1))).Distinct().Should().HaveCount(8,
            "cada combustible debe caer en una columna propia: dos marcas en la misma celda harían " +
            "ilegible cuál se marcó");
        valores.Select(c => Math.Round(c.Y0, 1)).Distinct().Should().ContainSingle(
            "los ocho combustibles comparten fila en el formulario");
    }

    /// <summary>
    /// HU #11641 — las casillas de subtrámite simultáneo caen en su celda del formulario, situadas
    /// por relación con las de prenda, que son las que la HU #11640 dejó ancladas:
    /// «5 CAMBIO DE COLOR» comparte columna con «11 INSCRIPC. PRENDA» y está una fila ARRIBA;
    /// «17 CAMBIO DE CARROCERÍA» comparte esa misma columna y está una fila ABAJO;
    /// «18 OTROS» comparte columna con «12 LEVANTA PRENDA» y está una fila abajo.
    /// («6 CAMBIO DE SERVICIO» no se declara: ver FurFieldMapper.MarkTramite.)
    ///
    /// <para>Sin esto, declarar las casillas nuevas repetiría el error que originó el Feature: una
    /// coordenada plausible que aterriza en la celda del vecino y produce un formulario bien formado
    /// y falso.</para>
    /// </summary>
    [Fact]
    public void Transformaciones_CaenEnSuCeldaRelativaALasDePrenda()
    {
        const FurTemplateFormat formato = FurTemplateFormat.Automotor;
        var constitucion = CeldaDe(formato, "requested_process_11");
        var levantamiento = CeldaDe(formato, "requested_process_12");

        var color = CeldaDe(formato, "requested_process_5");
        var carroceria = CeldaDe(formato, "requested_process_17");
        var otros = CeldaDe(formato, "requested_process_18");

        color.X0.Should().BeApproximately(constitucion.X0, 1.0, "«5 CAMBIO DE COLOR» está sobre «11 INSCRIPC. PRENDA»");
        color.Y1.Should().BeApproximately(constitucion.Y0, 1.0, "«5» está en la fila inmediatamente anterior a «11»");

        carroceria.X0.Should().BeApproximately(constitucion.X0, 1.0, "«17 CAMBIO DE CARROCERÍA» está bajo «11»");
        carroceria.Y0.Should().BeApproximately(constitucion.Y1, 1.0, "«17» está en la fila inmediatamente posterior a «11»");

        otros.X0.Should().BeApproximately(levantamiento.X0, 1.0, "«18 OTROS» está bajo «12 LEVANTA PRENDA»");
        otros.Y0.Should().BeApproximately(levantamiento.Y1, 1.0, "«18» está en la fila inmediatamente posterior a «12»");

        new[] { color, carroceria, otros, constitucion, levantamiento }
            .Select(c => (Math.Round(c.X0, 1), Math.Round(c.Y0, 1)))
            .Distinct().Should().HaveCount(5,
                "las seis casillas de la rejilla ocupan celdas distintas: dos marcas en la misma celda " +
                "harían imposible saber qué trámite se solicitó");
    }

    // ── Auditoría del barrido de recalibración (AC5) ──────────────────────────

    /// <summary>
    /// AC5 — ninguna casilla del manifiesto puede cruzar un borde impreso. Cubre de una vez todas las
    /// casillas de los tres formatos, que es el universo que tocó el barrido automático por anclas.
    /// Las que el formulario no recuadra (no hay celda que las encierre) se listan aparte: no son un
    /// fallo, pero deben quedar visibles para que nadie asuma que están verificadas.
    /// </summary>
    [Theory]
    [InlineData(FurTemplateFormat.Automotor)]
    [InlineData(FurTemplateFormat.Maquinaria)]
    [InlineData(FurTemplateFormat.Remolques)]
    public void TodaCasilla_CaeDentroDeSuCeldaImpresa(FurTemplateFormat formato)
    {
        var trazos = Trazos(formato);
        var casillas = FurFieldManifestLoader.LoadEmbedded(formato).Fields
            .Where(f => f.Type == FurFieldType.Checkbox)
            .ToList();

        casillas.Should().NotBeEmpty("el formato {0} declara casillas", formato);

        var desbordadas = new List<string>();
        var sinRecuadro = new List<string>();

        foreach (var f in casillas)
        {
            var celda = FurPlantillaGeometria.CeldaQueEncierra(trazos, f.X, f.Y, f.X + f.Size, f.Y + f.Size);
            if (celda is null)
            {
                sinRecuadro.Add(f.Id);
                continue;
            }

            var margen = celda.Value.MargenMinimo(f.X, f.Y, f.X + f.Size, f.Y + f.Size);
            if (margen < -ToleranciaProxy)
                desbordadas.Add(FormattableString.Invariant(
                    $"{f.Id} (caja {f.X:0.#},{f.Y:0.#}+{f.Size:0.#} desborda {celda.Value} en {-margen:0.##}pt)"));
        }

        desbordadas.Should().BeEmpty(
            "ninguna casilla del formato {0} debe cruzar un borde impreso del formulario.\n" +
            "  desbordadas:\n    {1}\n  (sin recuadro propio, no verificables por este método: {2})",
            formato, string.Join("\n    ", desbordadas), string.Join(", ", sinRecuadro));
    }
}
