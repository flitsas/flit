using System;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #12571 — <see cref="BusinessDayCalculator"/>: excluye sábado/domingo, sin festivos (ver
/// justificación en <see cref="IBusinessDayCalculator"/>: no se encontró ninguna utilidad de días
/// hábiles reutilizable en el repo).
/// </summary>
public sealed class BusinessDayCalculatorTests
{
    private static readonly BusinessDayCalculator Sut = new();

    // Lunes 2026-09-14.
    private static readonly DateTimeOffset Monday = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ZeroBusinessDays_DevuelveLaMismaFecha()
    {
        Sut.AddBusinessDays(Monday, 0).Should().Be(Monday);
    }

    [Theory]
    [InlineData(1, DayOfWeek.Tuesday)]
    [InlineData(2, DayOfWeek.Wednesday)]
    [InlineData(3, DayOfWeek.Thursday)]
    [InlineData(4, DayOfWeek.Friday)]
    public void SumaDentroDeLaMismaSemana_SaltaSoloElFinDeSemanaSiAplica(int dias, DayOfWeek esperado)
    {
        var resultado = Sut.AddBusinessDays(Monday, dias);

        resultado.DayOfWeek.Should().Be(esperado);
    }

    [Fact]
    public void CincoDiasHabilesDesdeElLunes_CruzaElFinDeSemana_CaeElLunesSiguiente()
    {
        var resultado = Sut.AddBusinessDays(Monday, 5);

        resultado.Date.Should().Be(Monday.Date.AddDays(7));
        resultado.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void PreservaLaHoraDelDia()
    {
        var start = new DateTimeOffset(2026, 9, 14, 14, 37, 22, TimeSpan.Zero);

        var resultado = Sut.AddBusinessDays(start, 3);

        resultado.TimeOfDay.Should().Be(start.TimeOfDay);
    }

    [Fact]
    public void DesdeUnSabado_ElPrimerDiaHabilEsElLunes()
    {
        var saturday = Monday.AddDays(-2); // Sábado anterior.

        var resultado = Sut.AddBusinessDays(saturday, 1);

        resultado.DayOfWeek.Should().Be(DayOfWeek.Monday);
        resultado.Date.Should().Be(Monday.Date);
    }

    [Fact]
    public void NegativeBusinessDays_LanzaArgumentOutOfRangeException()
    {
        var act = () => Sut.AddBusinessDays(Monday, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
