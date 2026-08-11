using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Preview;

/// <summary>
/// HU #11355 — muestras con DTOs sintéticos para las 2 plantillas de correo de Analítica
/// (informe programado y alerta).
///
/// Uso de ejemplo:
/// <code>
/// var (subject, html) = AnalyticsEmailPreviewSample.BuildScheduledReport();
/// // html contiene la tabla "Trámites por categoría" POBLADA con filas sintéticas
/// </code>
/// </summary>
public sealed class AnalyticsEmailPreviewSampleTests
{
    // El mismo valor fijo que usa el golden de producción (AnalyticsEmailGoldenTests, HU #11350)
    // para la regla de alerta — así el "golden de producción" que calculamos aquí es exactamente
    // el que ese golden congela, sin duplicar HTML a mano.
    private const string GoldenRuleName = "Rechazo OT alto";
    private const string GoldenScheduleName = "Informe diario";
    private const string GoldenReportType = "resumen";
    private const string GoldenPeriodLabel = "01/07/2026 - 07/07/2026";

    // Los composers de producción pasan estos campos por HtmlEncode antes de embeberlos en el
    // HTML — los marcadores llevan tildes, así que en el cuerpo aparecen como entidades
    // (p. ej. "&#193;"), no como el literal con tilde. El asunto NO pasa por HtmlEncode.
    private static readonly string EncodedPhScheduleName =
        System.Net.WebUtility.HtmlEncode(AnalyticsEmailPreviewSample.PhScheduleName);
    private static readonly string EncodedPhPeriodLabel =
        System.Net.WebUtility.HtmlEncode(AnalyticsEmailPreviewSample.PhPeriodLabel);
    private static readonly string EncodedPhRadicador1 =
        System.Net.WebUtility.HtmlEncode(AnalyticsEmailPreviewSample.PhRadicador1);
    private static readonly string EncodedPhRadicador2 =
        System.Net.WebUtility.HtmlEncode(AnalyticsEmailPreviewSample.PhRadicador2);
    private static readonly string EncodedPhRuleName =
        System.Net.WebUtility.HtmlEncode(AnalyticsEmailPreviewSample.PhRuleName);

    // --- AC1: tablas pobladas sin datos reales ----------------------------------------------

    [Fact]
    public void Informe_programado_tiene_las_tablas_pobladas_con_datos_sinteticos()
    {
        var (_, html) = AnalyticsEmailPreviewSample.BuildScheduledReport();

        html.Should().Contain("matriculas");
        html.Should().Contain("traspasos");
        html.Should().NotContain("Sin actividad registrada en el periodo.");
        html.Should().NotContain("Sin radicaciones en el periodo.");
    }

    [Fact]
    public void Informe_programado_usa_marcadores_para_los_nombres_de_radicador_no_personas_reales()
    {
        var (_, html) = AnalyticsEmailPreviewSample.BuildScheduledReport();

        html.Should().Contain(EncodedPhRadicador1);
        html.Should().Contain(EncodedPhRadicador2);
        html.Should().NotContain("Radicador Uno", "ese es el nombre sintético del golden de producción, no un marcador");
        html.Should().NotContain("Radicador Dos");
    }

    [Fact]
    public void Informe_programado_usa_marcadores_para_nombre_y_periodo()
    {
        var (subject, html) = AnalyticsEmailPreviewSample.BuildScheduledReport();

        html.Should().Contain(EncodedPhScheduleName);
        html.Should().Contain(EncodedPhPeriodLabel);
        subject.Should().Contain(AnalyticsEmailPreviewSample.PhScheduleName);
        subject.Should().Contain(AnalyticsEmailPreviewSample.PhPeriodLabel);
    }

    [Fact]
    public void Alerta_usa_marcador_para_el_nombre_de_la_regla_no_uno_real()
    {
        var (subject, html) = AnalyticsEmailPreviewSample.BuildAlert();

        subject.Should().Contain(AnalyticsEmailPreviewSample.PhRuleName);
        html.Should().Contain(EncodedPhRuleName);
        html.Should().NotContain(GoldenRuleName);
    }

    // --- AC2: render determinista, sin depender del reloj del sistema ----------------------

    [Fact]
    public void Alerta_es_deterministica_entre_llamadas_sucesivas()
    {
        var primera = AnalyticsEmailPreviewSample.BuildAlert();
        var segunda = AnalyticsEmailPreviewSample.BuildAlert();

        primera.Should().Be(segunda);
    }

    [Fact]
    public void Alerta_no_referencia_DateTimeOffset_UtcNow_ni_Now()
    {
        // El código fuente de la muestra no debe tomar el reloj del sistema en ningún punto:
        // si lo hiciera, dos ejecuciones en días distintos producirían cuerpos distintos.
        var rutaFuente = LocateSourceFile();

        File.Exists(rutaFuente).Should().BeTrue($"se esperaba encontrar el archivo fuente en {rutaFuente}");
        var contenido = File.ReadAllText(rutaFuente);

        contenido.Should().NotContain("DateTimeOffset.UtcNow");
        contenido.Should().NotContain("DateTimeOffset.Now");
        contenido.Should().NotContain("DateTime.UtcNow");
        contenido.Should().NotContain("DateTime.Now");
    }

    // --- AC4: la muestra no conoce el repositorio de analítica -------------------------------

    [Fact]
    public void La_muestra_no_referencia_IAnalyticsReadRepository_ni_IAlertMetricsReadRepository()
    {
        var tipo = typeof(AnalyticsEmailPreviewSample);

        var miembros = tipo.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType))
            .Concat(tipo.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Select(f => f.FieldType))
            .Select(t => t.Name);

        miembros.Should().NotContain(n => n.Contains("AnalyticsReadRepository", StringComparison.Ordinal));
        miembros.Should().NotContain(n => n.Contains("AlertMetricsReadRepository", StringComparison.Ordinal));
    }

    // --- AC3: misma función de composición que producción, con golden propio -----------------

    [Fact]
    public void Informe_programado_muestra_y_produccion_difieren_solo_en_los_valores_sustituidos()
    {
        var overview = new List<Flit.Analytics.Application.Dtos.CategoryMetricsDto>
        {
            new("matriculas", 7, new List<Flit.Analytics.Application.Dtos.StatusCountDto> { new("aprobado", 5), new("rechazado", 2) }),
            new("traspasos", 3, new List<Flit.Analytics.Application.Dtos.StatusCountDto> { new("radicado", 3) }),
        };
        var topProducers = new List<Flit.Analytics.Application.Dtos.TopProducerDto>
        {
            new(Guid.Empty, "Radicador Uno", 6, 5, 1),
            new(Guid.Empty, "Radicador Dos", 4, 2, 2),
        };

        var produccion = SchedulerEmailComposer.BuildScheduledReport(
            GoldenScheduleName, GoldenReportType, GoldenPeriodLabel, overview, topProducers);
        var muestra = AnalyticsEmailPreviewSample.BuildScheduledReport();

        var esperado = produccion.Html
            .Replace(GoldenScheduleName, EncodedPhScheduleName, StringComparison.Ordinal)
            .Replace("Radicador Uno", EncodedPhRadicador1, StringComparison.Ordinal)
            .Replace("Radicador Dos", EncodedPhRadicador2, StringComparison.Ordinal)
            .Replace(GoldenPeriodLabel, EncodedPhPeriodLabel, StringComparison.Ordinal);

        esperado.Should().Be(muestra.Html);
        muestra.Subject.Should().Contain(AnalyticsEmailPreviewSample.PhScheduleName);
    }

    [Fact]
    public void Alerta_muestra_usa_BuildAlert_de_produccion_con_el_mismo_calculo_de_hora_local()
    {
        var triggeredAtUtc = new DateTimeOffset(2026, 1, 15, 12, 30, 0, TimeSpan.Zero);
        var produccion = SchedulerEmailComposer.BuildAlert(
            GoldenRuleName, "rejection_rate_pct", "gt", 25m, 31.2m, 1440,
            triggeredAtUtc, ScheduleDueEvaluator.BogotaTimeZone);

        var muestra = AnalyticsEmailPreviewSample.BuildAlert();

        var esperado = produccion.Html.Replace(GoldenRuleName, EncodedPhRuleName, StringComparison.Ordinal);

        esperado.Should().Be(muestra.Html);
    }

    // --- AC3: golden propio de la muestra, fija la divergencia frente a producción -----------

    [Fact]
    public void Informe_programado_muestra_congela_asunto_y_cuerpo()
    {
        var (subject, html) = AnalyticsEmailPreviewSample.BuildScheduledReport();

        EmailGolden.Assert(ToMessage(subject, html), "informe-programado-muestra");
    }

    [Fact]
    public void Alerta_muestra_congela_asunto_y_cuerpo()
    {
        var (subject, html) = AnalyticsEmailPreviewSample.BuildAlert();

        EmailGolden.Assert(ToMessage(subject, html), "alerta-muestra");
    }

    private static EmailMessage ToMessage(string subject, string html) =>
        new("preview@flit.test", "Vista previa", subject, html);

    private static string LocateSourceFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("*.csproj").Length == 0)
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("No se encontró la raíz del repositorio de pruebas.");
        }

        // Sube un nivel más: de tests/Flit.Infrastructure.Tests a services/core-api, y de ahí a src.
        var repoRoot = dir.Parent!.Parent!;
        return Path.Combine(
            repoRoot.FullName, "src", "Flit.Infrastructure", "Notifications", "Preview", "AnalyticsEmailPreviewSample.cs");
    }
}
