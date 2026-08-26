using System.Globalization;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Queries.Domain;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, cuarta ola) — piezas puras de <see cref="IctOwnReportDocumentBuilder"/>:
/// clasificación de novedades por causa, estado de un webhook y el cálculo de porcentaje del
/// resumen. La carga cross-schema (<c>BuildNovedadesAsync</c>/<c>BuildAtascadosAsync</c>/
/// <c>BuildJobsAsync</c>/<c>BuildWebhooksAsync</c>) no es testeable sin Postgres real — mismo
/// límite ya documentado en <c>IctQueryRepositoryTests</c> para <c>AlertMetricsReadRepository</c>
/// e <c>IctQueryRepository</c>.
/// </summary>
public sealed class IctOwnReportDocumentBuilderTests
{
    /// <summary>Los tres estados de webhook, que deben cubrir las 4 combinaciones posibles.</summary>
    private static readonly string[] EstadosDeWebhook = ["Entregado", "Fallido", "Pendiente"];

    [Theory]
    [InlineData("El vehículo no tiene SOAT vigente", "SOAT")]
    [InlineData("soat vencido hace 3 meses", "SOAT")]
    [InlineData("RTM no vigente para la antigüedad del vehículo", "RTM")]
    [InlineData("Sanciones activas en RNMC", "RNMC")]
    [InlineData("Falta el documento del comprador", "Documento faltante")]
    public void ClassifyCausa_ComentarioConocido_DevuelveLaCausa(string comentario, string causaEsperada)
    {
        IctOwnReportDocumentBuilder.ClassifyCausa(comentario).Should().Be(causaEsperada);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Placa con formato inválido")]
    public void ClassifyCausa_SinCoincidencia_DevuelveNull(string? comentario)
    {
        IctOwnReportDocumentBuilder.ClassifyCausa(comentario).Should().BeNull();
    }

    [Fact]
    public void ClassifyCausa_ConVariasCausasEnElMismoTexto_DevuelveLaPrimeraDeLaListaConocida()
    {
        // "SOAT" está antes que "RTM" en CausasConocidas: un comentario que combina ambas se cuenta
        // una sola vez (no doble), por la primera que matchea — evita que un pre-trámite con dos
        // problemas infle el total del resumen.
        IctOwnReportDocumentBuilder.ClassifyCausa("Sin SOAT vigente y con RTM vencida")
            .Should().Be("SOAT");
    }

    [Theory]
    [InlineData(true, true, "Entregado")]
    [InlineData(true, false, "Fallido")]
    [InlineData(false, true, "Pendiente")]
    [InlineData(false, false, "Pendiente")]
    public void EstadoWebhook_CombinacionesDeNotificadoYRespuesta_DevuelveElEstadoCorrecto(
        bool isNotified, bool responseOk, string esperado)
    {
        IctOwnReportDocumentBuilder.EstadoWebhook(isNotified, responseOk).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0, 0, "0")]
    [InlineData(0, 10, "0")]
    [InlineData(5, 10, "50")]
    [InlineData(1, 3, "33.33")]
    public void Pct_CasosConocidos_RedondeaADosDecimalesEnCulturaInvariante(int part, int total, string esperado)
    {
        IctOwnReportDocumentBuilder.Pct(part, total).Should().Be(esperado);
    }

    /// <summary>
    /// Regresión de la HU #11630 (D3): las duraciones de <c>ict.job_runs</c> son milisegundos
    /// enteros y casi todas las corridas están por debajo de 5 ms. Redondear a 2 decimales de
    /// SEGUNDO —lo que se hacía antes— las aplastaba todas a 0; con décima de milisegundo el
    /// promedio real sobrevive.
    /// </summary>
    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(1d, 0.001d)]
    [InlineData(4d, 0.004d)]
    [InlineData(11.5d, 0.0115d)]
    [InlineData(60d, 0.06d)]
    [InlineData(412.9d, 0.4129d)]
    [InlineData(1500d, 1.5d)]
    public void MsToSeg_DuracionesSubMilisegundo_NoSeAplastanACero(double durationMs, double esperado)
    {
        IctOwnReportDocumentBuilder.MsToSeg(durationMs).Should().Be(esperado);
    }

    [Fact]
    public void MsToSeg_PromedioMenorA5ms_DejaDeRedondearseACero()
    {
        // El caso exacto medido en dev: 4 jobs con promedios de pocos milisegundos que la pantalla
        // mostraba como "0 s".
        IctOwnReportDocumentBuilder.MsToSeg(3.75d).Should().NotBe(0d);
    }

    /// <summary>
    /// El resumen por causa se agrega ahora en SQL (HU #11630), generado desde la MISMA lista de
    /// causas conocidas que usa <see cref="IctOwnReportDocumentBuilder.ClassifyCausa"/>. Este test
    /// fija que ambos caminos sigan cubriendo las mismas 4 causas y en el mismo orden de precedencia.
    /// </summary>
    [Theory]
    [InlineData("SOAT")]
    [InlineData("RTM")]
    [InlineData("RNMC")]
    [InlineData("DOCUMENTO")]
    public void ClassifyCausa_CubreLaMismaListaQueElAgregadoSql(string palabraClave)
    {
        IctOwnReportDocumentBuilder.ClassifyCausa($"texto con {palabraClave} adentro").Should().NotBeNull();
    }

    // ── D3: la evidencia de dev, fila por fila ────────────────────────────────────────────────

    /// <summary>
    /// Los promedios REALES medidos contra la base de dev para los 5 jobs del pipeline. Con la
    /// fórmula vieja —<c>Math.Round(ms / 1000d, 2)</c>— los cinco daban idénticamente 0,01 s y la
    /// pantalla los mostraba como si todos los jobs tardaran lo mismo. Este test es el testigo de la
    /// regresión: fija a la vez que el valor nuevo es correcto Y que el viejo los aplastaba.
    /// </summary>
    [Theory]
    [InlineData(6.48d, 0.0065d)]
    [InlineData(9.40d, 0.0094d)]
    [InlineData(10.19d, 0.0102d)]
    [InlineData(12.34d, 0.0123d)]
    [InlineData(13.41d, 0.0134d)]
    public void MsToSeg_PromediosRealesDeDev_QuedanDistinguiblesEntreSi(double promedioMs, double esperado)
    {
        IctOwnReportDocumentBuilder.MsToSeg(promedioMs).Should().Be(esperado);

        // La fórmula anterior: los 5 colapsaban al MISMO valor, que es el bug reportado.
        Math.Round(promedioMs / 1000d, 2).Should().Be(0.01d);
    }

    [Fact]
    public void MsToSeg_LosCincoJobsDeDev_YaNoDanTodosElMismoNumero()
    {
        double[] promediosMs = [6.48d, 9.40d, 10.19d, 12.34d, 13.41d];

        var nuevos = promediosMs.Select(IctOwnReportDocumentBuilder.MsToSeg).ToArray();

        nuevos.Should().OnlyHaveUniqueItems();
        nuevos.Should().AllSatisfy(v => v.Should().BeGreaterThan(0d));
    }

    /// <summary>Caso 9 de la spec: corridas de 1, 2, 3 y 4 ms — promedio 2,5 ms — daban 0 s.</summary>
    [Fact]
    public void MsToSeg_PromedioDeCorridasDeUnosPocosMilisegundos_NoEsCero()
    {
        double[] duracionesMs = [1d, 2d, 3d, 4d];

        IctOwnReportDocumentBuilder.MsToSeg(duracionesMs.Average()).Should().Be(0.0025d);
    }

    /// <summary>Caso 10 de la spec: una duración de 0 ms SÍ debe dar 0 — es un cero legítimo, no
    /// el artefacto de redondeo que se corrigió.</summary>
    [Fact]
    public void MsToSeg_DuracionCero_SigueSiendoCero()
    {
        IctOwnReportDocumentBuilder.MsToSeg(0d).Should().Be(0d);
    }

    /// <summary>Caso 11 de la spec (con el valor corregido): 11.500 ms son 11,5 s, no 1,15 s.</summary>
    [Fact]
    public void MsToSeg_DuracionDeVariosSegundos_ConservaLaEscala()
    {
        IctOwnReportDocumentBuilder.MsToSeg(11_500d).Should().Be(11.5d);
    }

    [Fact]
    public void MsToSeg_ResolucionMinimaEsLaDecimaDeMilisegundo()
    {
        // 0,05 ms redondea hacia arriba a 0,0001 s; por debajo de eso sí se pierde, y está bien:
        // duration_ms se guarda como ENTERO, así que un promedio nunca baja de ~0,1 ms real.
        IctOwnReportDocumentBuilder.MsToSeg(0.06d).Should().Be(0.0001d);
        IctOwnReportDocumentBuilder.MsToSeg(0.04d).Should().Be(0d);
    }

    // ── Porcentajes del resumen: nunca dividen por cero y siempre suman el total ───────────────

    /// <summary>
    /// Caso 18 de la spec (la parte aritmética): en un periodo VACÍO el resumen sale con sus filas
    /// en cero y <c>porcentajeTexto == "0"</c>, sin división por cero.
    /// </summary>
    [Fact]
    public void Pct_PeriodoVacio_DevuelveCeroSinDividirPorCero()
    {
        var act = () => IctOwnReportDocumentBuilder.Pct(0, 0);

        act.Should().NotThrow();
        IctOwnReportDocumentBuilder.Pct(0, 0).Should().Be("0");
    }

    /// <summary>
    /// Antes esta prueba re-escribía la regla en el propio test y no habría fallado si alguien
    /// cambiaba el builder. Ahora arma un resumen REAL con
    /// <see cref="IctOwnReportDocumentBuilder.BuildNovedadesReport"/> y suma los porcentajes que
    /// devuelve el código de producción.
    /// </summary>
    [Fact]
    public void BuildNovedadesReport_LosPorcentajesDelResumenSumanCien()
    {
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(5), PorCausa(soat: 40, rtm: 30, rnmc: 20, documento: 5, sinClasificar: 5),
            totalPeriodoAnterior: 0, page: 1, pageSize: 50);

        var suma = report.ResumenPorCausa
            .Select(r => double.Parse(r.PorcentajeTexto, CultureInfo.InvariantCulture))
            .Sum();

        suma.Should().BeApproximately(100d, 0.01d);
    }

    [Fact]
    public void Pct_UnaSolaCausaConcentraTodo_DevuelveCien()
    {
        IctOwnReportDocumentBuilder.Pct(2_500, 2_500).Should().Be("100");
    }

    [Fact]
    public void Pct_UniversoMayorAlTopeDelExcel_SeCalculaSobreElUniversoCompleto()
    {
        // D1/D2: el % del resumen se saca del universo del periodo (count(*) en SQL), no del tope
        // de 2.000 filas del documento. Con 2.500 novedades y 500 de una causa el resultado es 20,
        // no el 25 que daría calcularlo sobre las 2.000 traídas.
        IctOwnReportDocumentBuilder.Pct(500, 2_500).Should().Be("20");
    }

    // ── D1: la ventana de comparación de la variación ─────────────────────────────────────────

    /// <summary>
    /// <c>PreviousRange</c> es la BASE de la variación que D1 corrigió: si la ventana anterior no
    /// tuviera la misma longitud que la elegida, el "vs comparado" seguiría comparando peras con
    /// manzanas aunque los dos totales sean ya <c>count(*)</c>. No tenía ninguna prueba.
    /// </summary>
    [Theory]
    // Un solo día → el día anterior.
    [InlineData("2026-08-19", "2026-08-19", "2026-08-18", "2026-08-18")]
    // Una semana → la semana inmediatamente anterior, ambos extremos inclusivos.
    [InlineData("2026-08-13", "2026-08-19", "2026-08-06", "2026-08-12")]
    // Un mes completo → el mes anterior, con su propia longitud en días (31 → 31, no 30).
    [InlineData("2026-08-01", "2026-08-31", "2026-07-01", "2026-07-31")]
    // Cruce de año.
    [InlineData("2026-01-01", "2026-01-31", "2025-12-01", "2025-12-31")]
    // Año bisiesto: 29 días de febrero → los 29 anteriores.
    [InlineData("2024-02-01", "2024-02-29", "2024-01-03", "2024-01-31")]
    public void PreviousRange_DevuelveLaVentanaAnteriorDeLaMismaLongitud(
        string from, string to, string prevFromEsperado, string prevToEsperado)
    {
        var (prevFrom, prevTo) = IctOwnReportDocumentBuilder.PreviousRange(
            DateOnly.Parse(from, CultureInfo.InvariantCulture),
            DateOnly.Parse(to, CultureInfo.InvariantCulture));

        prevFrom.Should().Be(DateOnly.Parse(prevFromEsperado, CultureInfo.InvariantCulture));
        prevTo.Should().Be(DateOnly.Parse(prevToEsperado, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    [InlineData(365)]
    public void PreviousRange_ConservaLaLongitudYNoSeSolapaConElPeriodoActual(int dias)
    {
        var from = new DateOnly(2026, 8, 19).AddDays(-(dias - 1));
        var to = new DateOnly(2026, 8, 19);

        var (prevFrom, prevTo) = IctOwnReportDocumentBuilder.PreviousRange(from, to);

        // Misma cantidad de días: si no, la variación compara ventanas de tamaños distintos y vuelve
        // a inventar caídas/subidas — el mismo failure mode de D1, por otra vía.
        (prevTo.DayNumber - prevFrom.DayNumber + 1).Should().Be(dias);
        // Y pegada, sin solaparse: el día siguiente al fin del periodo previo es el inicio del actual.
        prevTo.AddDays(1).Should().Be(from);
    }

    // ── Contrato del tope del documento ───────────────────────────────────────────────────────

    /// <summary>
    /// El tope de filas del Excel es el que da sentido a <c>truncated</c>: tras la HU #11630
    /// significa "el documento no cabe entero" (<c>total &gt; MaxRows</c>), no "la lista en pantalla
    /// está cortada" — la pantalla ahora pide páginas. Se fija el valor para que un cambio de tope
    /// sea deliberado y no un ajuste silencioso.
    /// </summary>
    [Fact]
    public void MaxRows_EsElTopeDeLasDosMilFilasDelDocumento()
    {
        IctOwnReportDocumentBuilder.MaxRows.Should().Be(2_000);
    }

    /// <summary>
    /// La frontera exacta del cambio de semántica, ejercitando los TRES builders reales: con
    /// EXACTAMENTE <c>MaxRows</c> filas el documento cabe entero, así que <c>truncated</c> es falso.
    /// Con la regla anterior (<c>&gt;=</c>) daba verdadero y el Excel se rotulaba "top 2000" sin
    /// haber cortado nada. Esta prueba SÍ falla si alguien revierte el <c>&gt;</c> a <c>&gt;=</c>.
    /// </summary>
    [Theory]
    [InlineData(1_999, false)]
    [InlineData(2_000, false)]
    [InlineData(2_001, true)]
    public void Truncated_CambiaEnMaxRowsMasUno_NoEnMaxRows(int total, bool esperado)
    {
        IctOwnReportDocumentBuilder
            .BuildNovedadesReport(NovedadRows(3), PorCausa(soat: total), 0, 1, 50)
            .Truncated.Should().Be(esperado);

        IctOwnReportDocumentBuilder
            .BuildAtascadosReport(AtascadoRows(3), total, 1, 50)
            .Truncated.Should().Be(esperado);

        IctOwnReportDocumentBuilder
            .BuildWebhooksReport(WebhookRows((true, true)), total, 0, 1, 50, total, 0, 0)
            .Truncated.Should().Be(esperado);
    }

    // ── H1: el offset ya no desborda (regresión del hallazgo de la primera ronda) ──────────────

    /// <summary>
    /// El endpoint acota <c>pageSize</c> a 200 pero NO acota <c>page</c>. Con el offset calculado en
    /// <c>int</c>, <c>page=10.737.420</c> daba −2.147.483.496 y Postgres respondía "OFFSET must not
    /// be negative" → 500 en los 4 endpoints de lectura con solo escribir un número grande en la
    /// query. En <c>long</c> el producto máximo (~4,3e12) cabe de sobra.
    /// </summary>
    [Theory]
    [InlineData(10_737_419, 200, 2_147_483_600L)]
    [InlineData(10_737_420, 200, 2_147_483_800L)]
    [InlineData(int.MaxValue, 200, 429_496_729_200L)]
    [InlineData(int.MaxValue, 2_000, 4_294_967_292_000L)]
    public void NormalizePage_ConPaginaEnormementeGrande_ElOffsetNoDesbordaNiSeVuelveNegativo(
        int page, int pageSize, long offsetEsperado)
    {
        var (_, _, offset) = IctOwnReportDocumentBuilder.NormalizePage(page, pageSize);

        offset.Should().Be(offsetEsperado);
        offset.Should().BePositive();
        // Testigo del bug: cuando el producto se sale del rango de int, la cuenta vieja se volvía
        // negativa y Postgres rechazaba el OFFSET.
        if (offsetEsperado > int.MaxValue)
            unchecked((int)offsetEsperado).Should().BeNegative();
    }

    // ── Casos 12 y 13 de la spec: normalización de la página ──────────────────────────────────

    [Theory]
    // Caso 12: page=0 y page=-5 normalizan a 1, no revientan ni devuelven 400.
    [InlineData(0, 50, 1, 50, 0L)]
    [InlineData(-5, 50, 1, 50, 0L)]
    [InlineData(int.MinValue, 50, 1, 50, 0L)]
    // Caso 13: pageSize=0 (y negativos) se acotan a 1.
    [InlineData(1, 0, 1, 1, 0L)]
    [InlineData(1, -1, 1, 1, 0L)]
    // Caso 13: pageSize desmedido se acota al tope del builder (MaxRows); el acotado a 200 para
    // pantalla lo aplica el endpoint ANTES, así que la vista en vivo nunca llega aquí con 99999.
    [InlineData(1, 99_999, 1, 2_000, 0L)]
    [InlineData(1, 2_001, 1, 2_000, 0L)]
    // Caso 1: page=2&pageSize=25 salta exactamente las 25 primeras.
    [InlineData(2, 25, 2, 25, 25L)]
    [InlineData(3, 50, 3, 50, 100L)]
    // Caso 3: los valores por defecto que pasa el endpoint cuando no se manda nada.
    [InlineData(1, 50, 1, 50, 0L)]
    // El camino de exportación: una única página del tamaño del documento entero.
    [InlineData(1, 2_000, 1, 2_000, 0L)]
    public void NormalizePage_NormalizaEnVezDeRechazar(
        int page, int pageSize, int paginaEsperada, int tamanoEsperado, long offsetEsperado)
    {
        var resultado = IctOwnReportDocumentBuilder.NormalizePage(page, pageSize);

        resultado.Page.Should().Be(paginaEsperada);
        resultado.PageSize.Should().Be(tamanoEsperado);
        resultado.Offset.Should().Be(offsetEsperado);
    }

    [Fact]
    public void NormalizePage_ElTopeDePantallaCabeSiempreDentroDelTopeDelBuilder()
    {
        // Doble acotado: el endpoint corta a QueryLimits.MaxPageSize (200) y el builder a MaxRows
        // (2.000). Si alguien subiera el tope de pantalla por encima del del documento, el segundo
        // clamp empezaría a recortar en silencio lo que el endpoint ya dio por bueno.
        QueryLimits.MaxPageSize.Should().BeLessThanOrEqualTo(IctOwnReportDocumentBuilder.MaxRows);
        QueryLimits.DefaultPageSize.Should().BeLessThanOrEqualTo(QueryLimits.MaxPageSize);

        IctOwnReportDocumentBuilder.NormalizePage(1, QueryLimits.MaxPageSize).PageSize
            .Should().Be(QueryLimits.MaxPageSize);
    }

    // ── Caso 5: el resumen por causa cubre las 5 filas y suma el total ─────────────────────────

    [Fact]
    public void BuildNovedadesReport_ResumenTraeLasCuatroCausasConocidasMasOtraYSumanElTotal()
    {
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(25), PorCausa(soat: 900, rtm: 600, rnmc: 400, documento: 300, sinClasificar: 300),
            totalPeriodoAnterior: 400, page: 1, pageSize: 25);

        report.ResumenPorCausa.Select(r => r.Causa).Should().Equal(
            "SOAT", "RTM", "RNMC", "Documento faltante", "Otra/sin clasificar");
        report.ResumenPorCausa.Select(r => r.Cantidad).Should().Equal(900, 600, 400, 300, 300);
        // Ninguna novedad se descarta: lo que no matchea cae en "Otra/sin clasificar".
        report.ResumenPorCausa.Sum(r => r.Cantidad).Should().Be(report.Total);
        report.Total.Should().Be(2_500);
    }

    /// <summary>
    /// Caso 2/D2: <c>Total</c> es el UNIVERSO del periodo, nunca el largo de la página. Es
    /// exactamente el síntoma que se veía en dev (<c>total: 2000</c> con 38.865 filas reales).
    /// </summary>
    [Fact]
    public void BuildNovedadesReport_TotalEsElUniversoNoElLargoDeLaPagina()
    {
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(25), PorCausa(soat: 2_500),
            totalPeriodoAnterior: 400, page: 2, pageSize: 25);

        report.Detalle.Should().HaveCount(25);
        report.Total.Should().Be(2_500);
        report.Total.Should().NotBe(report.Detalle.Count);
        report.Page.Should().Be(2);
        report.PageSize.Should().Be(25);
    }

    /// <summary>
    /// Caso 6/D1: el total del periodo y el del periodo anterior salen de la MISMA clase de
    /// consulta (<c>count(*)</c>), así que la variación deja de ser un artefacto del tope de 2.000.
    /// Con 2.500 actuales contra 400 anteriores la variación es +525 %, no −94,9 %.
    /// </summary>
    [Fact]
    public void BuildNovedadesReport_ConMasDeDosMilNovedades_LaVariacionSubeEnVezDeCaer()
    {
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(50), PorCausa(soat: 2_500), totalPeriodoAnterior: 400, page: 1, pageSize: 50);

        report.Total.Should().Be(2_500);
        report.Total.Should().NotBe(IctOwnReportDocumentBuilder.MaxRows, "el tope ya no se cuela en el KPI");
        report.TotalPeriodoAnterior.Should().Be(400);
        report.Truncated.Should().BeTrue("el Excel sí corta a 2.000, aunque el KPI ya no");

        // La variación que deriva el frontend con esos dos números.
        var variacion = (report.Total - report.TotalPeriodoAnterior) * 100d / report.TotalPeriodoAnterior;
        variacion.Should().BeApproximately(525d, 0.01d);
        variacion.Should().BePositive();
    }

    /// <summary>Caso 7: el simétrico — un periodo actual pequeño contra uno anterior enorme sigue
    /// reportando su total real, sin tope.</summary>
    [Fact]
    public void BuildNovedadesReport_PeriodoActualPequenoContraAnteriorEnorme_ReportaSuTotalReal()
    {
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(50), PorCausa(soat: 100), totalPeriodoAnterior: 3_000, page: 1, pageSize: 50);

        report.Total.Should().Be(100);
        report.TotalPeriodoAnterior.Should().Be(3_000);
        report.Truncated.Should().BeFalse();
    }

    // ── Caso 14: página más allá del final ────────────────────────────────────────────────────

    [Fact]
    public void BuildNovedadesReport_PaginaMasAllaDelFinal_DetalleVacioPeroTotalIntacto()
    {
        // Una página fuera de rango trae 0 filas de SQL, pero el resumen y el total se agregan
        // aparte sobre el periodo completo: la pantalla debe seguir mostrando el universo real.
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(0), PorCausa(soat: 900, sinClasificar: 100),
            totalPeriodoAnterior: 400, page: 500, pageSize: 200);

        report.Detalle.Should().BeEmpty();
        report.Total.Should().Be(1_000);
        report.TotalPeriodoAnterior.Should().Be(400);
        report.Page.Should().Be(500);
        report.PageSize.Should().Be(200);
        report.ResumenPorCausa.Sum(r => r.Cantidad).Should().Be(1_000);
    }

    [Fact]
    public void BuildAtascadosReport_PaginaMasAllaDelFinal_DetalleVacioPeroTotalIntacto()
    {
        var report = IctOwnReportDocumentBuilder.BuildAtascadosReport(
            AtascadoRows(0), total: 742, page: 99, pageSize: 50);

        report.Detalle.Should().BeEmpty();
        report.Total.Should().Be(742);
        report.Page.Should().Be(99);
    }

    // ── Caso 18: periodo vacío ────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildNovedadesReport_PeriodoVacio_CincoFilasEnCeroSinDividirPorCero()
    {
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(0), PorCausa(), totalPeriodoAnterior: 0, page: 1, pageSize: 50);

        report.Total.Should().Be(0);
        report.Detalle.Should().BeEmpty();
        report.Truncated.Should().BeFalse();
        report.ResumenPorCausa.Should().HaveCount(5);
        report.ResumenPorCausa.Should().AllSatisfy(r =>
        {
            r.Cantidad.Should().Be(0);
            r.PorcentajeTexto.Should().Be("0");
        });
    }

    [Fact]
    public void BuildNovedadesReport_SqlNoDevuelveNingunaFila_ElResumenSigueTrayendoLasCincoFilas()
    {
        // Postgres no devuelve grupos vacíos: en un periodo sin novedades el GROUP BY no trae nada.
        // El resumen se arma igual desde CausasConocidas, así que la tabla nunca aparece vacía.
        var report = IctOwnReportDocumentBuilder.BuildNovedadesReport(
            NovedadRows(0), [], totalPeriodoAnterior: 0, page: 1, pageSize: 50);

        report.ResumenPorCausa.Should().HaveCount(5);
        report.Total.Should().Be(0);
    }

    [Fact]
    public void BuildWebhooksReport_PeriodoVacio_ContadoresEnCero()
    {
        var report = IctOwnReportDocumentBuilder.BuildWebhooksReport(
            WebhookRows(), total: 0, totalPeriodoAnterior: 0, page: 1, pageSize: 50,
            totalEntregados: 0, totalFallidos: 0, totalPendientes: 0);

        report.Total.Should().Be(0);
        report.Detalle.Should().BeEmpty();
        report.Truncated.Should().BeFalse();
        (report.TotalEntregados + report.TotalFallidos + report.TotalPendientes).Should().Be(report.Total);
    }

    // ── Contadores de webhooks: la partición por estado ───────────────────────────────────────

    /// <summary>
    /// La invariante que garantiza el SQL: los 3 contadores por estado PARTICIONAN el total. Se
    /// sostiene a nivel de esquema porque <c>is_notified</c> y <c>response_ok</c> son
    /// <c>NOT NULL DEFAULT false</c>, así que no hay una quinta combinación con NULL que se escape
    /// de los tres <c>FILTER</c>.
    /// </summary>
    [Theory]
    [InlineData(120, 80, 30, 10)]
    [InlineData(1, 1, 0, 0)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(2_500, 2_000, 300, 200)]
    public void BuildWebhooksReport_LosTresContadoresParticionanElTotal(
        int total, int entregados, int fallidos, int pendientes)
    {
        var report = IctOwnReportDocumentBuilder.BuildWebhooksReport(
            WebhookRows((true, true), (true, false), (false, false)),
            total, totalPeriodoAnterior: 0, page: 1, pageSize: 50,
            totalEntregados: entregados, totalFallidos: fallidos, totalPendientes: pendientes);

        report.TotalEntregados.Should().Be(entregados);
        report.TotalFallidos.Should().Be(fallidos);
        report.TotalPendientes.Should().Be(pendientes);
        (report.TotalEntregados + report.TotalFallidos + report.TotalPendientes).Should().Be(report.Total);
    }

    /// <summary>
    /// El SQL de los contadores (<c>EstadoWebhookSql</c>) se GENERA enumerando las 4 combinaciones
    /// de (<c>is_notified</c>, <c>response_ok</c>) y quedándose con las que <see cref="EstadoWebhook"/>
    /// clasifica en cada estado — mismo patrón que <c>CausaCaseSql</c>. Para que esos tres
    /// <c>count(*) FILTER</c> sumen el total, la función C# tiene que ser una PARTICIÓN del dominio:
    /// cada combinación en exactamente un estado, ninguna fuera. Eso es lo que se fija aquí, sobre
    /// la función de producción.
    /// </summary>
    [Fact]
    public void EstadoWebhook_ParticionaLasCuatroCombinacionesEnLosTresEstados()
    {
        (bool IsNotified, bool ResponseOk)[] combinaciones =
            [(false, false), (false, true), (true, false), (true, true)];

        var clasificadas = combinaciones
            .Select(c => IctOwnReportDocumentBuilder.EstadoWebhook(c.IsNotified, c.ResponseOk))
            .ToList();

        // Ninguna combinación queda sin estado ni cae en un cuarto estado inventado.
        clasificadas.Should().OnlyContain(e => EstadosDeWebhook.Contains(e));
        clasificadas.Should().HaveCount(combinaciones.Length);
        // Y los tres estados están representados: si uno quedara vacío, su predicado SQL se
        // generaría como "false" y el contador saldría siempre en 0.
        clasificadas.Distinct().Should().BeEquivalentTo(EstadosDeWebhook);
    }

    [Fact]
    public void BuildWebhooksReport_ElDetalleUsaLaMismaReglaDeEstadoQueLosContadores()
    {
        var report = IctOwnReportDocumentBuilder.BuildWebhooksReport(
            WebhookRows((true, true), (true, false), (false, true), (false, false)),
            total: 4, totalPeriodoAnterior: 0, page: 1, pageSize: 50,
            totalEntregados: 1, totalFallidos: 1, totalPendientes: 2);

        report.Detalle.Select(d => d.Estado).Should().Equal(
            "Entregado", "Fallido", "Pendiente", "Pendiente");
    }

    // ── Atascados: universo vs página ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildAtascadosReport_TotalEsElUniversoYLaPaginaEsSoloLaPagina()
    {
        var report = IctOwnReportDocumentBuilder.BuildAtascadosReport(
            AtascadoRows(50), total: 3_400, page: 1, pageSize: 50);

        report.Detalle.Should().HaveCount(50);
        report.Total.Should().Be(3_400);
        report.Truncated.Should().BeTrue();
        report.Page.Should().Be(1);
        report.PageSize.Should().Be(50);
    }

    [Fact]
    public void BuildAtascadosReport_DistingueQuienEstaEsperandoQue()
    {
        var creado = DateTimeOffset.UtcNow.AddDays(-3);
        var rows = new List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)>
        {
            ("ABC123", "VIN1", "RAD1", true, creado),
            ("DEF456", "VIN2", "RAD2", false, creado),
        };

        var report = IctOwnReportDocumentBuilder.BuildAtascadosReport(rows, total: 2, page: 1, pageSize: 50);

        report.Detalle[0].Esperando.Should().Be("Validación de negocio");
        report.Detalle[1].Esperando.Should().Be("Fuente externa (RUNT/RNMC/SOAT)");
        report.Detalle.Should().AllSatisfy(d => d.DiasTranscurridos.Should().BeApproximately(3d, 0.01d));
    }

    // ── "ict_jobs": el último informe que quedaba sin builder puro ─────────────────────────────

    /// <summary>
    /// Caso 2 de la spec original. Cuando <c>Total</c> se acumulaba en el mismo bucle que llenaba
    /// <c>porJob</c> esta aserción no podía fallar y por eso la marqué como tautológica; ahora
    /// <c>Total</c> se DERIVA de <c>porJob</c> dentro del builder, así que sí prueba algo.
    /// </summary>
    [Fact]
    public void BuildJobsReport_TotalEsLaSumaDeLasCorridasDelResumen()
    {
        var porJob = new[]
        {
            JobResumen("ict.enriquecer", corridas: 1_000, promedioMs: 6.48d, fueraDeSla: 4),
            JobResumen("ict.notificar", corridas: 900, promedioMs: 9.40d, fueraDeSla: 3),
            JobResumen("ict.validar", corridas: 600, promedioMs: 13.41d, fueraDeSla: 3),
        };

        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            porJob, IncumplidasRows(10), totalFueraDeSla: 10, totalPeriodoAnterior: 400,
            page: 1, pageSize: 50);

        report.Total.Should().Be(2_500);
        report.ResumenPorJob.Sum(j => j.Corridas).Should().Be(report.Total);
    }

    [Fact]
    public void BuildJobsReport_PeriodoVacio_TotalCeroYSinTruncar()
    {
        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            [], [], totalFueraDeSla: 0, totalPeriodoAnterior: 0, page: 1, pageSize: 50);

        report.Total.Should().Be(0);
        report.Truncated.Should().BeFalse();
        report.TotalFueraDeSla.Should().Be(0);
        report.ResumenPorJob.Should().BeEmpty();
        report.CorridasFueraDeSla.Should().BeEmpty();
    }

    /// <summary>
    /// El bug H2: <c>Truncated</c> se calculaba sobre las corridas TOTALES, pero la única lista que
    /// el Excel puede cortar en este informe es la de corridas fuera de SLA. Con 2.500 corridas de
    /// las que solo 10 incumplen, el documento sale entero y la pantalla no debe avisar de un
    /// recorte que no ocurrió.
    /// </summary>
    [Fact]
    public void BuildJobsReport_MuchasCorridasPeroPocasFueraDeSla_NoSeMarcaComoTruncado()
    {
        var porJob = new[] { JobResumen("ict.enriquecer", corridas: 2_500, promedioMs: 10.19d, fueraDeSla: 10) };

        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            porJob, IncumplidasRows(10), totalFueraDeSla: 10, totalPeriodoAnterior: 400,
            page: 1, pageSize: 50);

        report.Total.Should().Be(2_500);
        report.Total.Should().BeGreaterThan(IctOwnReportDocumentBuilder.MaxRows);
        report.Truncated.Should().BeFalse("el Excel corta la lista de fuera de SLA, y solo hay 10");
        report.TotalFueraDeSla.Should().Be(10);
    }

    /// <summary>La frontera exacta: es <c>&gt;</c>, no <c>&gt;=</c>, y se mide sobre
    /// <c>totalFueraDeSla</c>, no sobre el total de corridas.</summary>
    [Theory]
    [InlineData(1_999, false)]
    [InlineData(2_000, false)]
    [InlineData(2_001, true)]
    public void BuildJobsReport_TruncatedCambiaEnMaxRowsMasUnoDeCorridasFueraDeSla(
        int totalFueraDeSla, bool esperado)
    {
        var porJob = new[] { JobResumen("ict.validar", corridas: 50_000, promedioMs: 12.34d, fueraDeSla: totalFueraDeSla) };

        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            porJob, IncumplidasRows(50), totalFueraDeSla, totalPeriodoAnterior: 0, page: 1, pageSize: 50);

        report.Truncated.Should().Be(esperado);
        // Y no se contagia del total de corridas, que aquí está muy por encima del tope.
        report.Total.Should().Be(50_000);
    }

    [Fact]
    public void BuildJobsReport_LaListaEsLaPaginaYTotalFueraDeSlaEsSuUniverso()
    {
        var porJob = new[] { JobResumen("ict.notificar", corridas: 9_000, promedioMs: 9.40d, fueraDeSla: 640) };

        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            porJob, IncumplidasRows(50), totalFueraDeSla: 640, totalPeriodoAnterior: 8_000,
            page: 2, pageSize: 50);

        report.CorridasFueraDeSla.Should().HaveCount(50);
        report.TotalFueraDeSla.Should().Be(640);
        report.TotalFueraDeSla.Should().NotBe(report.CorridasFueraDeSla.Count);
        // Los dos universos del informe son distintos y ninguno es el largo de la página.
        report.Total.Should().Be(9_000);
        report.Page.Should().Be(2);
        report.PageSize.Should().Be(50);
    }

    [Fact]
    public void BuildJobsReport_PaginaMasAllaDelFinal_ListaVaciaPeroTotalesIntactos()
    {
        // Con la guarda nueva de LoadJobsAsync (offset >= totalFueraDeSla) la consulta de detalle ni
        // se lanza: el builder recibe la lista vacía y aun así debe reportar los universos reales.
        var porJob = new[] { JobResumen("ict.validar", corridas: 3_000, promedioMs: 6.48d, fueraDeSla: 120) };

        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            porJob, [], totalFueraDeSla: 120, totalPeriodoAnterior: 2_800, page: 500, pageSize: 200);

        report.CorridasFueraDeSla.Should().BeEmpty();
        report.Total.Should().Be(3_000);
        report.TotalFueraDeSla.Should().Be(120);
        report.TotalPeriodoAnterior.Should().Be(2_800);
        report.Page.Should().Be(500);
    }

    /// <summary>
    /// D3 de punta a punta en la forma que llega a pantalla: los 5 jobs reales de dev conservan
    /// promedios DISTINTOS dentro del DTO. Antes los cinco salían "0,01 s" y la tabla parecía decir
    /// que todos los jobs tardaban exactamente lo mismo.
    /// </summary>
    [Fact]
    public void BuildJobsReport_LosCincoJobsDeDevConservanPromediosDistintosEnElDto()
    {
        double[] promediosMs = [6.48d, 9.40d, 10.19d, 12.34d, 13.41d];
        var porJob = promediosMs
            .Select((ms, i) => JobResumen($"ict.job{i}", corridas: 100, promedioMs: ms, fueraDeSla: 1))
            .ToArray();

        var report = IctOwnReportDocumentBuilder.BuildJobsReport(
            porJob, IncumplidasRows(5), totalFueraDeSla: 5, totalPeriodoAnterior: 0, page: 1, pageSize: 50);

        report.ResumenPorJob.Select(j => j.DuracionPromedioSeg).Should().OnlyHaveUniqueItems();
        report.ResumenPorJob.Should().AllSatisfy(j => j.DuracionPromedioSeg.Should().BeGreaterThan(0d));
        report.Total.Should().Be(500);
    }

    // ── Fábricas de datos de prueba ───────────────────────────────────────────────────────────

    /// <summary>Arma un <see cref="IctJobResumenDto"/> igual que lo hace <c>LoadJobsAsync</c> al leer
    /// el GROUP BY: duraciones por <c>MsToSeg</c> y el incumplimiento por <c>Pct</c>.</summary>
    private static IctJobResumenDto JobResumen(string job, int corridas, double promedioMs, int fueraDeSla) =>
        new(job, corridas,
            IctOwnReportDocumentBuilder.MsToSeg(promedioMs),
            IctOwnReportDocumentBuilder.MsToSeg(promedioMs * 3),
            IctOwnReportDocumentBuilder.Pct(fueraDeSla, corridas));

    private static IReadOnlyList<IctJobIncumplidoDto> IncumplidasRows(int cantidad) =>
        [.. Enumerable.Range(1, cantidad).Select(i => new IctJobIncumplidoDto(
            $"ict.job{i % 5}", "timeout", IctOwnReportDocumentBuilder.MsToSeg(11_500d),
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero).AddMinutes(-i)))];

    private static IReadOnlyList<(string Causa, int Cantidad)> PorCausa(
        int soat = 0, int rtm = 0, int rnmc = 0, int documento = 0, int sinClasificar = 0) =>
    [
        ("SOAT", soat),
        ("RTM", rtm),
        ("RNMC", rnmc),
        ("Documento faltante", documento),
        // La cadena vacía es lo que devuelve el ELSE de CausaCaseSql: "Otra/sin clasificar".
        (string.Empty, sinClasificar),
    ];

    private static List<(string? Placa, string? Vin, string? Radicado, string? Comentarios, DateTimeOffset CreatedAt)>
        NovedadRows(int cantidad) =>
        [.. Enumerable.Range(1, cantidad).Select(i => (
            (string?)$"ABC{i:000}", (string?)$"VIN{i:000}", (string?)$"RAD{i:000}",
            (string?)"El vehículo no tiene SOAT vigente",
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero).AddMinutes(-i)))];

    private static List<(string? Placa, string? Vin, string? Radicado, bool EsperandoNegocio, DateTimeOffset CreatedAt)>
        AtascadoRows(int cantidad) =>
        [.. Enumerable.Range(1, cantidad).Select(i => (
            (string?)$"ABC{i:000}", (string?)$"VIN{i:000}", (string?)$"RAD{i:000}", i % 2 == 0,
            DateTimeOffset.UtcNow.AddDays(-i)))];

    private static List<(Guid IdTransaction, string? Radicado, bool IsNotified, bool ResponseOk,
        int Attempts, string? TargetUrl, DateTimeOffset CreatedAt)>
        WebhookRows(params (bool IsNotified, bool ResponseOk)[] combinaciones) =>
        [.. combinaciones.Select((c, i) => (
            Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"), (string?)$"RAD{i:000}",
            c.IsNotified, c.ResponseOk, 1, (string?)"https://gestor.example/webhook",
            new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero).AddMinutes(-i)))];
}
