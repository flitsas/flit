using Flit.Infrastructure.Analytics.Scheduling;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// Reportes 2.0 HU-D — vencimiento de informes programados (§8): hora local America/Bogota
/// (UTC-5 todo el año), día correcto según frequency, y NO re-envío dentro de la misma
/// ventana (día / semana ISO / mes) cuando last_sent_at ya quedó sellado.
/// </summary>
public sealed class ScheduleDueEvaluatorTests
{
    private static readonly TimeZoneInfo Bogota = ScheduleDueEvaluator.BogotaTimeZone;

    // Martes 2026-07-07 12:30 UTC = 07:30 en Bogotá (UTC-5).
    private static readonly DateTimeOffset TuesdayNoonUtc = new(2026, 7, 7, 12, 30, 0, TimeSpan.Zero);

    // ── daily ──────────────────────────────────────────────────────────────

    [Fact]
    public void Daily_vence_en_su_hora_local_de_Bogota()
    {
        ScheduleDueEvaluator.IsDue("daily", null, null, sendHour: 7,
            lastSentAtUtc: null, TuesdayNoonUtc, Bogota).Should().BeTrue();
    }

    [Fact]
    public void Daily_no_vence_fuera_de_su_hora()
    {
        // 12:30 UTC son las 07:30 en Bogotá: con send_hour = 12 NO debe vencer (la hora es local).
        ScheduleDueEvaluator.IsDue("daily", null, null, sendHour: 12,
            lastSentAtUtc: null, TuesdayNoonUtc, Bogota).Should().BeFalse();
    }

    [Fact]
    public void Daily_no_reenvia_si_last_sent_at_es_del_mismo_dia_local()
    {
        // Sellado 20 minutos antes, mismo día local → misma ventana → no vence.
        var lastSent = TuesdayNoonUtc.AddMinutes(-20);

        ScheduleDueEvaluator.IsDue("daily", null, null, 7, lastSent, TuesdayNoonUtc, Bogota)
            .Should().BeFalse();
    }

    [Fact]
    public void Daily_vence_de_nuevo_al_dia_siguiente()
    {
        var lastSent = TuesdayNoonUtc.AddDays(-1);

        ScheduleDueEvaluator.IsDue("daily", null, null, 7, lastSent, TuesdayNoonUtc, Bogota)
            .Should().BeTrue();
    }

    [Fact]
    public void La_conversion_de_zona_cruza_el_dia_correctamente()
    {
        // 03:10 UTC del miércoles = 22:10 del MARTES en Bogotá → schedule de las 22 vence el martes.
        var utc = new DateTimeOffset(2026, 7, 8, 3, 10, 0, TimeSpan.Zero);

        ScheduleDueEvaluator.IsDue("weekly", dayOfWeek: 2 /* martes */, null, sendHour: 22,
            lastSentAtUtc: null, utc, Bogota).Should().BeTrue();
    }

    // ── weekly ─────────────────────────────────────────────────────────────

    [Fact]
    public void Weekly_vence_solo_en_su_dia_de_semana()
    {
        // 2026-07-07 es martes (dayOfWeek = 2 con 0 = domingo).
        ScheduleDueEvaluator.IsDue("weekly", 2, null, 7, null, TuesdayNoonUtc, Bogota)
            .Should().BeTrue();
        ScheduleDueEvaluator.IsDue("weekly", 1, null, 7, null, TuesdayNoonUtc, Bogota)
            .Should().BeFalse();
    }

    [Fact]
    public void Weekly_no_reenvia_dentro_de_la_misma_semana_ISO()
    {
        // Sellado el lunes de la misma semana ISO → no vence el martes.
        var lastSent = TuesdayNoonUtc.AddDays(-1);

        ScheduleDueEvaluator.IsDue("weekly", 2, null, 7, lastSent, TuesdayNoonUtc, Bogota)
            .Should().BeFalse();
    }

    [Fact]
    public void Weekly_vence_en_la_semana_ISO_siguiente()
    {
        var lastSent = TuesdayNoonUtc.AddDays(-7);

        ScheduleDueEvaluator.IsDue("weekly", 2, null, 7, lastSent, TuesdayNoonUtc, Bogota)
            .Should().BeTrue();
    }

    // ── monthly ────────────────────────────────────────────────────────────

    [Fact]
    public void Monthly_vence_solo_en_su_dia_del_mes()
    {
        ScheduleDueEvaluator.IsDue("monthly", null, 7, 7, null, TuesdayNoonUtc, Bogota)
            .Should().BeTrue();
        ScheduleDueEvaluator.IsDue("monthly", null, 8, 7, null, TuesdayNoonUtc, Bogota)
            .Should().BeFalse();
    }

    [Fact]
    public void Monthly_no_reenvia_dentro_del_mismo_mes()
    {
        var lastSent = TuesdayNoonUtc.AddDays(-3); // 4 de julio, mismo mes

        ScheduleDueEvaluator.IsDue("monthly", null, 7, 7, lastSent, TuesdayNoonUtc, Bogota)
            .Should().BeFalse();
    }

    [Fact]
    public void Monthly_vence_al_mes_siguiente()
    {
        var lastSent = TuesdayNoonUtc.AddMonths(-1);

        ScheduleDueEvaluator.IsDue("monthly", null, 7, 7, lastSent, TuesdayNoonUtc, Bogota)
            .Should().BeTrue();
    }

    // ── periodo vencido ────────────────────────────────────────────────────

    [Fact]
    public void Periodo_daily_es_el_dia_anterior()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(TuesdayNoonUtc, Bogota);

        var (from, to) = ScheduleDueEvaluator.GetElapsedPeriod("daily", nowLocal);

        from.Should().Be(new DateOnly(2026, 7, 6));
        to.Should().Be(new DateOnly(2026, 7, 6));
    }

    [Fact]
    public void Periodo_weekly_es_la_semana_ISO_anterior_de_lunes_a_domingo()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(TuesdayNoonUtc, Bogota); // martes 7 de julio

        var (from, to) = ScheduleDueEvaluator.GetElapsedPeriod("weekly", nowLocal);

        from.Should().Be(new DateOnly(2026, 6, 29)); // lunes anterior
        to.Should().Be(new DateOnly(2026, 7, 5));    // domingo anterior
    }

    [Fact]
    public void Periodo_monthly_es_el_mes_calendario_anterior()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(TuesdayNoonUtc, Bogota);

        var (from, to) = ScheduleDueEvaluator.GetElapsedPeriod("monthly", nowLocal);

        from.Should().Be(new DateOnly(2026, 6, 1));
        to.Should().Be(new DateOnly(2026, 6, 30));
    }
}
