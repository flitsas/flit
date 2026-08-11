using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Analytics.Scheduling;

namespace Flit.Infrastructure.Notifications.Preview;

/// <summary>
/// Muestra de las 2 plantillas de correo de Analítica (informe programado y alerta) para
/// previsualización (HU #11355, Feature #11347).
/// </summary>
/// <remarks>
/// <para>
/// Sigue el mismo patrón que <see cref="SecurityEmailPreviewSample"/> (HU #11354): cada dato
/// variable se rellena con un MARCADOR visible en mayúsculas entre corchetes — nunca con un
/// nombre, documento, correo o teléfono inventado (AC1, Ley 1581). Los conteos numéricos de las
/// tablas SÍ son datos sintéticos (no son PII), porque son necesarios para poblar las tablas
/// (AC1 de esta HU).
/// </para>
/// <para>
/// AC3 — cada muestra se compone con la MISMA función que usa producción
/// (<c>SchedulerEmailComposer.BuildScheduledReport</c> / <c>BuildAlert</c>): esta clase solo
/// decide qué DTOs sintéticos y qué marcadores le pasa, nunca construye el HTML por su cuenta.
/// </para>
/// <para>
/// AC2 — la fecha de disparo de la alerta es una constante fija (<see cref="TriggeredAtUtc"/>),
/// nunca el reloj del sistema en tiempo real: el render debe ser determinista entre ejecuciones y
/// no depender de cuándo se corra la suite.
/// </para>
/// <para>
/// AC4 — esta clase no conoce ni referencia <c>IAnalyticsReadRepository</c> ni
/// <c>IAlertMetricsReadRepository</c>: los DTOs de entrada son literales sintéticos construidos a
/// mano, así que una muestra jamás consulta la base analítica.
/// </para>
/// </remarks>
public static class AnalyticsEmailPreviewSample
{
    // Marcadores en mayúsculas entre corchetes — ningún dato de persona, real ni inventado.
    public const string PhScheduleName = "[ACÁ VA EL NOMBRE DEL INFORME PROGRAMADO]";
    public const string PhPeriodLabel = "[ACÁ VA EL PERIODO DEL INFORME]";
    public const string PhRadicador1 = "[ACÁ VA EL NOMBRE DEL RADICADOR 1]";
    public const string PhRadicador2 = "[ACÁ VA EL NOMBRE DEL RADICADOR 2]";
    public const string PhRuleName = "[ACÁ VA EL NOMBRE DE LA REGLA DE ALERTA]";

    private const string SyntheticReportType = "resumen";
    private const string SyntheticMetric = "rejection_rate_pct";
    private const string SyntheticOperator = "gt";
    private const decimal SyntheticThreshold = 25m;
    private const decimal SyntheticValue = 31.2m;
    private const int SyntheticWindowMinutes = 1440;

    // Constante fija (AC2) — jamás el reloj del sistema en tiempo real: el render de la muestra
    // debe ser idéntico en cualquier ejecución, sin importar cuándo se corra la suite.
    private static readonly DateTimeOffset TriggeredAtUtc = new(2026, 1, 15, 12, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Muestra del informe programado (plantilla <c>analytics.scheduled-report</c>) con tablas de
    /// KPIs por categoría y top de radicadores POBLADAS con DTOs sintéticos (AC1).
    /// </summary>
    public static (string Subject, string Html) BuildScheduledReport()
    {
        var overview = new List<CategoryMetricsDto>
        {
            new("matriculas", 7, new List<StatusCountDto> { new("aprobado", 5), new("rechazado", 2) }),
            new("traspasos", 3, new List<StatusCountDto> { new("radicado", 3) }),
        };
        var topProducers = new List<TopProducerDto>
        {
            new(Guid.Empty, PhRadicador1, 6, 5, 1),
            new(Guid.Empty, PhRadicador2, 4, 2, 2),
        };

        return SchedulerEmailComposer.BuildScheduledReport(
            PhScheduleName, SyntheticReportType, PhPeriodLabel, overview, topProducers);
    }

    /// <summary>
    /// Muestra de la alerta (plantilla <c>analytics.alert</c>) con métrica, operador, umbral,
    /// valor y ventana sintéticos, y fecha de disparo FIJA (AC2 — determinista, no depende del
    /// reloj del sistema).
    /// </summary>
    public static (string Subject, string Html) BuildAlert() =>
        SchedulerEmailComposer.BuildAlert(
            PhRuleName,
            SyntheticMetric,
            SyntheticOperator,
            SyntheticThreshold,
            SyntheticValue,
            SyntheticWindowMinutes,
            TriggeredAtUtc,
            ScheduleDueEvaluator.BogotaTimeZone);
}
