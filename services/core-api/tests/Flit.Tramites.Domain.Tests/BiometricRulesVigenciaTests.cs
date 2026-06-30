using System;
using FluentAssertions;
using Flit.Tramites.Domain.Entities;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Vigencia de la identidad APROBADA (30 días calendario desde la aprobación, en hora de Colombia):
/// fecha de fin de vigencia y días restantes que la grilla del submódulo de Validaciones muestra.
/// El día de aprobación es el día 1 y vence en el día 31 (consistente con <see cref="BiometricRules.EsAprobadaVigente"/>).
/// </summary>
public sealed class BiometricRulesVigenciaTests
{
    // 2026-06-20 10:00 hora de Colombia (UTC-5).
    private static readonly DateTimeOffset Aprobacion = new(2026, 6, 20, 10, 0, 0, TimeSpan.FromHours(-5));

    private static ProcedureInstanceBiometricValidation Aprobada(DateTimeOffset? validadoAt) =>
        new() { Estado = BiometricEstados.Aprobado, ValidadoAt = validadoAt };

    [Fact]
    public void FechaFinVigencia_EsElDiaDeAprobacionMas30()
    {
        var fin = BiometricRules.FechaFinVigencia(Aprobada(Aprobacion));

        // 2026-06-20 + 30 días = 2026-07-20 (día de expiración, ya NO vigente).
        fin.Should().NotBeNull();
        fin!.Value.ToOffset(TimeSpan.FromHours(-5)).Date.Should().Be(new DateTime(2026, 7, 20));
    }

    [Fact]
    public void FechaFinVigencia_SinAprobacion_EsNull()
    {
        BiometricRules.FechaFinVigencia(Aprobada(validadoAt: null)).Should().BeNull();
    }

    [Fact]
    public void DiasRestantes_DiaDeAprobacion_Reporta30()
    {
        var dias = BiometricRules.DiasRestantesVigencia(Aprobada(Aprobacion), Aprobacion);

        dias.Should().Be(BiometricRules.VigenciaDias); // 30
    }

    [Fact]
    public void DiasRestantes_UltimoDiaVigente_Reporta1()
    {
        // Día 30 (2026-07-19): aún vigente, le resta 1 día.
        var hoy = Aprobacion.AddDays(29);

        BiometricRules.DiasRestantesVigencia(Aprobada(Aprobacion), hoy).Should().Be(1);
    }

    [Fact]
    public void DiasRestantes_DiaDeExpiracion_Reporta0()
    {
        // Día 31 (2026-07-20): ya no es vigente.
        var hoy = Aprobacion.AddDays(30);

        BiometricRules.DiasRestantesVigencia(Aprobada(Aprobacion), hoy).Should().Be(0);
        BiometricRules.EsAprobadaVigente(Aprobada(Aprobacion), hoy).Should().BeFalse();
    }

    [Fact]
    public void DiasRestantes_YaVencidaHaceTiempo_NoBajaDeCero()
    {
        var hoy = Aprobacion.AddDays(90);

        BiometricRules.DiasRestantesVigencia(Aprobada(Aprobacion), hoy).Should().Be(0);
    }

    [Fact]
    public void DiasRestantes_SinAprobacion_EsNull()
    {
        BiometricRules.DiasRestantesVigencia(Aprobada(validadoAt: null), Aprobacion).Should().BeNull();
    }
}
