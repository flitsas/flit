using Flit.Infrastructure.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.RuntConfirmation;

/// <summary>HU #12309 AC1 — decisión pura de «hoy ya tocó» en hora de Bogotá, con recuperación si el proceso estaba caído a la hora.</summary>
public sealed class RuntConfirmationSchedulerDueTests
{
    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.CreateCustomTimeZone("Bogota", TimeSpan.FromHours(-5), "Bogota", "Bogota");

    // 2026-09-10 02:00 Bogotá = 07:00Z
    private static readonly DateTimeOffset RunAtTodayUtc = new(2026, 9, 10, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AntesDeLaHora_NoToca()
    {
        RuntConfirmationSchedulerProcessor.IsDue("02:00", null, RunAtTodayUtc.AddMinutes(-1), Bogota).Should().BeFalse();
    }

    [Fact]
    public void ALaHora_SinCorridaPrevia_Toca()
    {
        RuntConfirmationSchedulerProcessor.IsDue("02:00", null, RunAtTodayUtc, Bogota).Should().BeTrue();
    }

    [Fact]
    public void YaCorrioHoy_NoRepite()
    {
        RuntConfirmationSchedulerProcessor.IsDue("02:00", RunAtTodayUtc.AddMinutes(1), RunAtTodayUtc.AddHours(5), Bogota).Should().BeFalse();
    }

    [Fact]
    public void CorrioAyer_YElProcesoEstuvoCaidoALaHora_TocaAlVolver()
    {
        RuntConfirmationSchedulerProcessor.IsDue("02:00", RunAtTodayUtc.AddDays(-1), RunAtTodayUtc.AddHours(9), Bogota).Should().BeTrue();
    }

    [Fact]
    public void ElDiaSeCuentaEnBogotaNoEnUtc()
    {
        // 2026-09-10 22:30 Bogotá = 2026-09-11 03:30Z: sigue siendo el día 10 en Bogotá, y hoy ya corrió.
        var lastToday = RunAtTodayUtc.AddMinutes(2);
        var lateEvening = new DateTimeOffset(2026, 9, 11, 3, 30, 0, TimeSpan.Zero);
        RuntConfirmationSchedulerProcessor.IsDue("02:00", lastToday, lateEvening, Bogota).Should().BeFalse();
    }

    [Fact]
    public void HoraInvalida_CaeAlDefault()
    {
        RuntConfirmationSchedulerProcessor.IsDue("no", null, RunAtTodayUtc, Bogota).Should().BeTrue();
        RuntConfirmationSchedulerProcessor.IsDue("no", null, RunAtTodayUtc.AddMinutes(-1), Bogota).Should().BeFalse();
    }
}
