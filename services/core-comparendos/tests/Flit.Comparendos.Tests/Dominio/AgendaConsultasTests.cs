using Flit.Comparendos.Domain.Calendario;
using Flit.Comparendos.Domain.Ingesta;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

/// <summary>HU #13521 — agenda de consultas por empresa (hora de Colombia).</summary>
public sealed class AgendaConsultasTests
{
    private static readonly CalendarioHabil Calendario = CalendarioHabil.Calculado(2026, 2027);

    private static AgendaConsultas Horas(bool habiles, params string[] horas) =>
        new(ModoAgenda.HorasFijas, horas.Select(h => AgendaConsultas.Hora(h)!.Value).ToList(), null, null, null, habiles);

    private static AgendaConsultas CadaHora(bool habiles = false) =>
        new(ModoAgenda.Intervalo, [], 60, new TimeOnly(6, 0), new TimeOnly(20, 0), habiles);

    [Fact]
    public void HorasFijas_TocaSoloEnEsosMinutos()
    {
        var a = Horas(false, "07:00", "13:00");
        a.Toca(new DateTime(2026, 10, 13, 7, 0, 0), Calendario).Should().BeTrue();
        a.Toca(new DateTime(2026, 10, 13, 7, 1, 0), Calendario).Should().BeFalse();
        a.Toca(new DateTime(2026, 10, 13, 13, 0, 0), Calendario).Should().BeTrue();
        a.Proxima(new DateTime(2026, 10, 13, 7, 0, 0), Calendario).Should().Be(new DateTime(2026, 10, 13, 13, 0, 0));
        a.Proxima(new DateTime(2026, 10, 13, 14, 0, 0), Calendario).Should().Be(new DateTime(2026, 10, 14, 7, 0, 0));
        a.Describir().Should().Be("Todos los días a las 07:00, 13:00");
    }

    [Fact]
    public void Intervalo_DentroDeLaVentana()
    {
        var a = CadaHora();
        a.Toca(new DateTime(2026, 10, 13, 6, 0, 0), Calendario).Should().BeTrue();
        a.Toca(new DateTime(2026, 10, 13, 9, 0, 0), Calendario).Should().BeTrue();
        a.Toca(new DateTime(2026, 10, 13, 9, 30, 0), Calendario).Should().BeFalse();
        a.Toca(new DateTime(2026, 10, 13, 20, 0, 0), Calendario).Should().BeTrue();
        a.Toca(new DateTime(2026, 10, 13, 21, 0, 0), Calendario).Should().BeFalse();
        a.Proxima(new DateTime(2026, 10, 13, 20, 0, 0), Calendario).Should().Be(new DateTime(2026, 10, 14, 6, 0, 0));
    }

    [Fact]
    public void SoloDiasHabiles_SaltaFinesDeSemanaYFestivos()
    {
        var a = Horas(true, "07:00");
        a.Toca(new DateTime(2026, 10, 12, 7, 0, 0), Calendario).Should().BeFalse("12 de octubre es festivo");
        a.Toca(new DateTime(2026, 10, 17, 7, 0, 0), Calendario).Should().BeFalse("sábado");
        a.Proxima(new DateTime(2026, 10, 16, 8, 0, 0), Calendario).Should().Be(new DateTime(2026, 10, 19, 7, 0, 0));
    }

    [Fact]
    public void Validar()
    {
        Horas(false).Validar().Should().ContainSingle();
        Horas(false, "07:00", "07:00").Validar().Should().Contain("Hay horas repetidas.");
        new AgendaConsultas(ModoAgenda.Intervalo, [], 10, new TimeOnly(6, 0), new TimeOnly(20, 0), false).Validar().Should().ContainSingle();
        new AgendaConsultas(ModoAgenda.Intervalo, [], 60, new TimeOnly(20, 0), new TimeOnly(6, 0), false).Validar().Should().ContainSingle();
        CadaHora().Validar().Should().BeEmpty();
    }
}
