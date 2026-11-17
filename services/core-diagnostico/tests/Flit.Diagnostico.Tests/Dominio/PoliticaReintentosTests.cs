using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13737 (F03-H6), D-12; ajuste 2026-11-11 Punto 3 (decisión 3.1) — política de reintentos: un transitorio con
/// <c>intentos_auto</c> menor que <c>MaxReintentos</c> (por defecto 3) vuelve a la cola con <c>disponible_desde = ahora + espera</c>,
/// una espera FIJA configurable (por defecto 2 min); un definitivo o los reintentos agotados no se reintentan. El instante sale de
/// un reloj inyectable (<see cref="RelojManual"/>), sin esperas. <c>MaxReintentos</c> ∈ [0, 3]; la espera ∈ [1 s, 1 h].
/// </summary>
/// <example>
/// <code>
/// new PoliticaReintentos(3).Decidir("TIMEOUT", intentosAuto: 0, ahora); // Reintentar en ahora + 2 min
/// </code>
/// </example>
public sealed class PoliticaReintentosTests
{
    // Punto 3 AC1 — por defecto 3 reintentos y 2 min de espera fija.
    [Fact]
    public void Por_defecto_son_3_reintentos_con_espera_de_2_minutos()
    {
        PoliticaReintentos.PorDefecto.Should().Be(3);
        PoliticaReintentos.EsperaPorDefecto.Should().Be(TimeSpan.FromMinutes(2));
        new PoliticaReintentos(PoliticaReintentos.PorDefecto).Espera.Should().Be(TimeSpan.FromMinutes(2), "sin espera explícita, la de por defecto");
    }

    // Punto 3 AC1 — tres transitorios, cada uno a 2 min (espera fija, no creciente); el cuarto queda en ERROR.
    [Fact]
    public void Transitorio_se_reintenta_3_veces_cada_2_minutos_y_luego_queda_en_ERROR()
    {
        var reloj = new RelojManual();
        var ahora = reloj.GetUtcNow();
        var tres = new PoliticaReintentos(3);

        tres.Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(true, ahora.AddMinutes(2), 1));
        tres.Decidir("RED", 1, ahora).Should().Be(new DecisionReintento(true, ahora.AddMinutes(2), 2), "la espera es fija, no crece");
        tres.Decidir("HTTP_5XX", 2, ahora).Should().Be(new DecisionReintento(true, ahora.AddMinutes(2), 3));
        tres.Decidir("TIMEOUT", 3, ahora).Should().Be(new DecisionReintento(false, null, 3), "agotados los 3 reintentos queda en ERROR");

        new PoliticaReintentos(0).Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(false, null, 0), "con 0 no se reintenta");

        reloj.Avanzar(TimeSpan.FromMinutes(1));
        tres.Decidir("TIMEOUT", 0, reloj.GetUtcNow()).DisponibleDesde.Should().Be(ahora.AddMinutes(3), "la espera cuenta desde el reloj dado");
    }

    // Punto 3 AC1 — la espera es configurable.
    [Fact]
    public void La_espera_es_configurable()
    {
        var ahora = DateTimeOffset.UnixEpoch;
        var politica = new PoliticaReintentos(2, TimeSpan.FromSeconds(30));

        politica.Espera.Should().Be(TimeSpan.FromSeconds(30));
        politica.Decidir("TIMEOUT", 0, ahora).Should().Be(new DecisionReintento(true, ahora.AddSeconds(30), 1));
        politica.Decidir("TIMEOUT", 1, ahora).Should().Be(new DecisionReintento(true, ahora.AddSeconds(30), 2));
        politica.Decidir("TIMEOUT", 2, ahora).Reintentar.Should().BeFalse();
    }

    // Punto 3 AC2 — un definitivo no se reintenta aunque queden reintentos.
    [Theory]
    [InlineData("RESPUESTA_INVALIDA")]
    [InlineData("NO_CONECTADO")]
    [InlineData(null)]
    public void Definitivo_no_se_reintenta(string? codigo) =>
        new PoliticaReintentos(3).Decidir(codigo, 0, DateTimeOffset.UnixEpoch).Should().Be(new DecisionReintento(false, null, 0));

    // D-12 — MaxReintentos fuera de [0, 3] no es una política válida.
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void MaxReintentos_fuera_de_rango_lanza(int max) =>
        FluentActions.Invoking(() => new PoliticaReintentos(max)).Should().Throw<ArgumentOutOfRangeException>();

    // Punto 3 — una espera fuera de [1 s, 1 h] no es válida.
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3601)]
    public void Espera_fuera_de_rango_lanza(int segundos) =>
        FluentActions.Invoking(() => new PoliticaReintentos(3, TimeSpan.FromSeconds(segundos))).Should().Throw<ArgumentOutOfRangeException>();
}
