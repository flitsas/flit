using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Analytics.Scheduling;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// HU #11352 — <see cref="SchedulerEmailComposer"/> es pura: para la misma entrada, misma salida
/// (asunto y cuerpo). No depende de reloj de pared, cultura del hilo ni estado mutable — por eso
/// se puede llamar dos veces seguidas y comparar carácter a carácter.
/// </summary>
public sealed class SchedulerEmailComposerTests
{
    [Fact]
    public void BuildScheduledReport_es_pura()
    {
        var overview = new List<CategoryMetricsDto>
        {
            new("matriculas", 7, new List<StatusCountDto> { new("aprobado", 5), new("rechazado", 2) }),
        };
        var topProducers = new List<TopProducerDto> { new(Guid.Empty, "Radicador Uno", 6, 5, 1) };

        var first = SchedulerEmailComposer.BuildScheduledReport(
            "Informe diario", "resumen", "06/07/2026", overview, topProducers);
        var second = SchedulerEmailComposer.BuildScheduledReport(
            "Informe diario", "resumen", "06/07/2026", overview, topProducers);

        first.Should().Be(second);
        first.Subject.Should().Be("[FLIT] Informe diario — 06/07/2026");
    }

    [Fact]
    public void BuildAlert_es_pura()
    {
        var triggeredAtUtc = new DateTimeOffset(2026, 7, 7, 12, 30, 0, TimeSpan.Zero);
        var timeZone = AnalyticsSchedulerProcessor.BogotaTimeZone;

        var first = SchedulerEmailComposer.BuildAlert(
            "Rechazo OT alto", "rejection_rate_pct", "gt", 25m, 31.2m, 1440, triggeredAtUtc, timeZone);
        var second = SchedulerEmailComposer.BuildAlert(
            "Rechazo OT alto", "rejection_rate_pct", "gt", 25m, 31.2m, 1440, triggeredAtUtc, timeZone);

        first.Should().Be(second);
        first.Subject.Should().Be("[FLIT] Alerta: Rechazo OT alto");
    }
}
