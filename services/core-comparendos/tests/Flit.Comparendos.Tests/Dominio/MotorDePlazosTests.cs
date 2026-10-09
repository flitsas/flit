using Flit.Comparendos.Domain.Calendario;
using Flit.Comparendos.Domain.Plazos;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

public sealed class MotorDePlazosTests
{
    private static readonly MotorDePlazos Motor = new(CalendarioHabil.Calculado(2025, 2030));

    // Jueves 1-oct-2026. Hábiles siguientes: 2, 5, 6, 7, 8, 9, 13 (12 es festivo), 14, 15, 16, 19 (el 11)…
    private static readonly DateOnly Notificacion = new(2026, 10, 1);

    [Fact]
    public void Fotomulta_cuenta_todo_desde_la_notificacion()
    {
        var f = Motor.Calcular(TipoImposicion.Fotomulta, new DateOnly(2026, 9, 1), Notificacion);

        f.LimiteGestion.Should().Be(new DateOnly(2026, 10, 19));
        f.Limite50.Should().Be(new DateOnly(2026, 10, 19));
        f.Limite25.Should().Be(new DateOnly(2026, 11, 10));
        f.Caducidad.Should().Be(new DateOnly(2027, 10, 1));
        // 1-oct-2029 es lunes hábil.
        f.Prescripcion.Should().Be(new DateOnly(2029, 10, 1));
    }

    [Fact]
    public void Caducidad_en_dia_no_habil_se_corre_al_siguiente_habil()
    {
        // 2-oct-2027 es sábado: la caducidad pasa al lunes 4.
        var f = Motor.Calcular(TipoImposicion.Fotomulta, null, new DateOnly(2026, 10, 2));
        f.Caducidad.Should().Be(new DateOnly(2027, 10, 4));
    }

    [Fact]
    public void Fisico_cuenta_el_descuento_desde_la_imposicion()
    {
        var f = Motor.Calcular(TipoImposicion.Fisico, Notificacion, Notificacion);

        f.Limite50.Should().Be(new DateOnly(2026, 10, 8));
        f.Limite25.Should().Be(new DateOnly(2026, 10, 30));
    }

    [Fact]
    public void Sin_notificacion_no_corre_ningun_plazo()
    {
        var f = Motor.Calcular(TipoImposicion.Fotomulta, new DateOnly(2026, 9, 1), null);
        var s = Motor.Evaluar(f, new DateOnly(2026, 10, 5));

        f.LimiteGestion.Should().BeNull();
        s.Estado.Should().Be(EstadoLegal.SinNotificar);
        s.DescuentoVigente.Should().Be(Descuento.Ninguno);
    }

    [Theory]
    [InlineData("2026-10-02", EstadoLegal.EnTiempo, Descuento.Cincuenta, false)]
    [InlineData("2026-10-14", EstadoLegal.EnTiempo, Descuento.Cincuenta, false)]
    [InlineData("2026-10-16", EstadoLegal.EnTiempo, Descuento.Cincuenta, true)]
    [InlineData("2026-10-19", EstadoLegal.EnTiempo, Descuento.Cincuenta, true)]
    [InlineData("2026-10-20", EstadoLegal.Vencido, Descuento.Veinticinco, false)]
    [InlineData("2026-11-11", EstadoLegal.Vencido, Descuento.Ninguno, false)]
    [InlineData("2027-10-01", EstadoLegal.Vencido, Descuento.Ninguno, false)]
    [InlineData("2027-10-02", EstadoLegal.Caducado, Descuento.Ninguno, false)]
    [InlineData("2029-10-02", EstadoLegal.Prescrito, Descuento.Ninguno, false)]
    public void Evaluar_fotomulta(string hoy, EstadoLegal estado, Descuento descuento, bool proximo)
    {
        var f = Motor.Calcular(TipoImposicion.Fotomulta, null, Notificacion);
        var s = Motor.Evaluar(f, DateOnly.Parse(hoy, System.Globalization.CultureInfo.InvariantCulture));

        s.Estado.Should().Be(estado);
        s.DescuentoVigente.Should().Be(descuento);
        s.ProximoAVencer.Should().Be(proximo);
    }
}
