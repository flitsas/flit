using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13739 (F03-H7), D-10 — vigencia del dato del mismo día con corte a medianoche hora Colombia (UTC−5, sin horario de
/// verano): vale hasta las 23:59:59 y vence a las 00:00:00 del día siguiente aunque no hayan pasado 24 h. Los instantes salen de
/// un reloj inyectable (<see cref="RelojManual"/>), sin depender de la zona del host.
/// </summary>
/// <example>
/// <code>
/// VigenciaDelDia.Vigente(obtenidoEn: datoDeLas0800, ahora: lasQuince); // true, mismo día en hora Colombia
/// </code>
/// </example>
public sealed class VigenciaDelDiaTests
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);

    // D-10 — un dato de las 08:00 vale todo el día hasta las 23:59:59 hora Colombia; a las 00:00:00 ya no.
    [Fact]
    public void Dato_de_hoy_vale_hasta_las_23_59_59_hora_Colombia()
    {
        var dato = new DateTimeOffset(2026, 10, 21, 8, 0, 0, Colombia);

        VigenciaDelDia.Vigente(dato, new DateTimeOffset(2026, 10, 21, 15, 0, 0, Colombia)).Should().BeTrue("mismo día a las 15:00");
        VigenciaDelDia.Vigente(dato, new DateTimeOffset(2026, 10, 21, 23, 59, 59, Colombia)).Should().BeTrue("a las 23:59:59 sigue vigente");
        VigenciaDelDia.Vigente(dato, new DateTimeOffset(2026, 10, 21, 23, 59, 59, Colombia).AddMilliseconds(999)).Should().BeTrue();
        VigenciaDelDia.Vigente(dato, new DateTimeOffset(2026, 10, 22, 0, 0, 0, Colombia)).Should().BeFalse("a medianoche vence");
        VigenciaDelDia.InicioDelDia(new DateTimeOffset(2026, 10, 21, 15, 0, 0, Colombia))
            .Should().Be(new DateTimeOffset(2026, 10, 21, 0, 0, 0, Colombia));
        VigenciaDelDia.InicioDelDia(new DateTimeOffset(2026, 10, 21, 15, 0, 0, Colombia)).Offset.Should().Be(Colombia);
        VigenciaDelDia.FinDelDia(new DateTimeOffset(2026, 10, 21, 15, 0, 0, Colombia))
            .Should().Be(new DateTimeOffset(2026, 10, 22, 0, 0, 0, Colombia));

        // Un dato de las 19:30 hora Colombia ya es el día siguiente en UTC (00:30Z): manda el día de Colombia, no el de UTC.
        var nocturno = new DateTimeOffset(2026, 10, 21, 19, 30, 0, Colombia);
        nocturno.UtcDateTime.Day.Should().Be(22, "precondición: en UTC ya es el 22");
        VigenciaDelDia.Vigente(nocturno.ToUniversalTime(), new DateTimeOffset(2026, 10, 21, 23, 59, 59, Colombia).ToUniversalTime())
            .Should().BeTrue("19:30 y 23:59:59 son el mismo día en Colombia aunque UTC diga otra cosa");
        VigenciaDelDia.Vigente(nocturno.ToUniversalTime(), new DateTimeOffset(2026, 10, 21, 20, 0, 0, Colombia)).Should().BeTrue();
        VigenciaDelDia.InicioDelDia(nocturno.ToUniversalTime()).Should().Be(new DateTimeOffset(2026, 10, 21, 0, 0, 0, Colombia));

        // Con el reloj inyectable, sin mirar la zona del host.
        var reloj = new RelojManual();
        var ahora = reloj.GetUtcNow();
        VigenciaDelDia.Vigente(ahora, ahora).Should().BeTrue("el dato recién obtenido está vigente");
        reloj.Avanzar(VigenciaDelDia.FinDelDia(ahora) - ahora);
        VigenciaDelDia.Vigente(ahora, reloj.GetUtcNow()).Should().BeFalse("al llegar a las 00:00 Colombia ya venció");
    }

    // D-10 — el dato de ayer vence a las 00:00:00 hora Colombia aunque no hayan pasado 24 h (23:50 → 00:05 son 15 minutos).
    [Fact]
    public void Dato_de_ayer_vence_a_las_00_00_00_hora_Colombia()
    {
        var ayer = new DateTimeOffset(2026, 10, 21, 23, 50, 0, Colombia);

        VigenciaDelDia.Vigente(ayer, new DateTimeOffset(2026, 10, 21, 23, 59, 59, Colombia)).Should().BeTrue();
        VigenciaDelDia.Vigente(ayer, new DateTimeOffset(2026, 10, 22, 0, 0, 0, Colombia)).Should().BeFalse("vence a las 00:00:00");
        VigenciaDelDia.Vigente(ayer, new DateTimeOffset(2026, 10, 22, 0, 5, 0, Colombia)).Should().BeFalse("15 minutos después, pero otro día");
        VigenciaDelDia.Vigente(ayer, new DateTimeOffset(2026, 10, 22, 0, 5, 0, Colombia).ToUniversalTime()).Should().BeFalse("en UTC igual");

        // 19:30 Colombia de ayer (00:30Z de hoy en UTC) vence a las 00:00 Colombia de hoy, no a las 00:00Z.
        var nocturno = new DateTimeOffset(2026, 10, 21, 19, 30, 0, Colombia).ToUniversalTime();
        VigenciaDelDia.Vigente(nocturno, new DateTimeOffset(2026, 10, 22, 4, 59, 59, TimeSpan.Zero)).Should().BeTrue("23:59:59 Colombia");
        VigenciaDelDia.Vigente(nocturno, new DateTimeOffset(2026, 10, 22, 5, 0, 0, TimeSpan.Zero)).Should().BeFalse("00:00:00 Colombia = 05:00Z");
        VigenciaDelDia.InicioDelDia(new DateTimeOffset(2026, 10, 22, 5, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateTimeOffset(2026, 10, 22, 0, 0, 0, Colombia));
    }
}
